namespace ProcessorModule
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Npgsql;
    using Npgsql.CrateDb;

    class Program
    {
        static ILogger<Program> Logger;
        static TelemetryClient TelemetryClient;
        static string ModuleName;
        static string ConnectionString;

        class Telemetry
        {
            public long Timestamp { get; set; }
        }

        static void Main(string[] args)
        {
            ModuleName = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
            ConnectionString = Environment.GetEnvironmentVariable("CRATEDB_CONNECTION_STRING");

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            IServiceCollection services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder
                    .AddConfiguration(config.GetSection("Logging"))
                    .AddConsole();
            });
            services.AddApplicationInsightsTelemetryWorkerService(Environment.GetEnvironmentVariable("APPLICATION_INSIGHTS_INSTRUMENTATION_KEY"));
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Logger = serviceProvider.GetRequiredService<ILogger<ProcessorModule.Program>>();
            TelemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();

            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            NpgsqlDatabaseInfo.RegisterFactory(new CrateDbDatabaseInfoFactory());

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub.
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            Logger.LogInformation("PipeMessage");

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            if (!string.IsNullOrEmpty(messageString))
            {
                try
                {
                    var moduleClient = userContext as ModuleClient;
                    var telemetry = JsonConvert.DeserializeObject<Telemetry>(messageString);
                    long timeStamp = telemetry.Timestamp;
                    Logger.LogDebug($"Timestamp {timeStamp}");

                    Stopwatch sw = new Stopwatch();
                    using (var conn = new NpgsqlConnection(ConnectionString))
                    {
                        Logger.LogDebug($"Message: {messageString}");
                        await conn.OpenAsync();
                        using (NpgsqlCommand cmd = new NpgsqlCommand())
                        {
                            cmd.Connection = conn;
                            cmd.CommandText = "SELECT payload FROM doc.test_table1 WHERE ts = @ts";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("ts", timeStamp);
                            try
                            {
                                sw.Reset();
                                sw.Start();
                                int size = 0;
                                await using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    do
                                    {
                                        while (await reader.ReadAsync())
                                        {
                                            size += reader.GetString(0).Length;
                                        }
                                    }
                                    while (await reader.NextResultAsync());
                                }
                                sw.Stop();
                                Logger.LogDebug($"Done {timeStamp} {sw.Elapsed.TotalMilliseconds} {size}");

                                TimeSpan ts = sw.Elapsed;
                                var perf = new MetricTelemetry();
                                perf.Name = ModuleName;
                                perf.Sum = ts.TotalMilliseconds;
                                TelemetryClient.TrackMetric(perf);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"{ex}");
                            }
                        }
                    }

                    using (var pipeMessage = new Message(messageBytes))
                    {
                        foreach (var prop in message.Properties)
                        {
                            pipeMessage.Properties.Add(prop.Key, prop.Value);
                        }
                        await moduleClient.SendEventAsync("output1", pipeMessage);

                        Logger.LogDebug("Received message sent");
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError($"{ex}");
                }
            }
            else
            {
                Logger.LogDebug("Message is empty");
            }
            return MessageResponse.Completed;
        }
    }
}
