namespace SourceModule
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
        class Telemetry
        {
            public long Timestamp { get; set; }
        }

        static ILogger<Program> Logger;
        static TelemetryClient TelemetryClient;
        static DesiredPropertiesData DesiredProperties;
        static string ModuleName;
        static string ConnectionString;

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

            Logger = serviceProvider.GetRequiredService<ILogger<SourceModule.Program>>();
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
            Logger.LogInformation("IoT Hub module client initialized.");

            Twin moduleTwin = await ioTHubModuleClient.GetTwinAsync();
            TwinCollection moduleTwinCollection = moduleTwin.Properties.Desired;
            DesiredProperties = new DesiredPropertiesData(moduleTwinCollection);
#pragma warning disable 4014
            MainLoop(ioTHubModuleClient);
#pragma warning restore 4014
        }

        static string CreateMessage()
        {
            JArray ja = new JArray();
            MemoryStream stream = new MemoryStream();
            for (int i = 0; i < DesiredProperties.RowCount; i++)
            {
                JObject jo = new JObject();
                jo.Add("timestamp", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                for (int j = 0; j < DesiredProperties.FieldLength; j++)
                {
                    jo.Add($"field{j}", new string('*', DesiredProperties.DataLength));
                }
                ja.Add(jo);
            }
            JObject message = new JObject();
            message.Add("data", ja);
            return message.ToString();
        }

        static async Task MainLoop(ModuleClient moduleClient)
        {
            Logger.LogInformation("Start MainLoop");
            Logger.LogDebug($"Connection String: {ConnectionString}");
            NpgsqlDatabaseInfo.RegisterFactory(new CrateDbDatabaseInfoFactory());
            Stopwatch sw = new Stopwatch();
            while (true)
            {
                Logger.LogDebug("before new NpgsqlConnection");
                using (var conn = new NpgsqlConnection(ConnectionString))
                {
                    string messageString = CreateMessage();
                    Logger.LogDebug($"Message Size: {messageString.Length}");
                    conn.Open();
                    using (var cmd = new NpgsqlCommand())
                    {
                        TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                        long timestamp = (long)t.TotalMilliseconds;
                        cmd.Connection = conn;
                        cmd.CommandText = "INSERT INTO doc.test_table1 (ts, payload) VALUES (@ts, @payload)";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("ts", timestamp);
                        cmd.Parameters.AddWithValue("payload", messageString);
                        try
                        {
                            sw.Reset();
                            sw.Start();
                            bool succeeded = await cmd.ExecuteNonQueryAsync() == 1;
                            sw.Stop();
                            if (!succeeded)
                            {
                                Logger.LogError("Failed to write message to Crate DB");
                            }
                            else
                            {
                                Logger.LogDebug("Successfully written message to Crate DB");
                            }
                            TimeSpan ts = sw.Elapsed;
                            var perf = new MetricTelemetry();
                            perf.Name = ModuleName;
                            perf.Sum = ts.TotalMilliseconds;
                            TelemetryClient.TrackMetric(perf);

                            Telemetry telemetry = new Telemetry
                            {
                                Timestamp = timestamp
                            };
                            string json = JsonConvert.SerializeObject(telemetry);
                            byte[] messageBytes = Encoding.UTF8.GetBytes(json);
                            using (Message message = new Message(messageBytes))
                            {
                                await moduleClient.SendEventAsync("output1", message).ConfigureAwait(false);
                            }

                            Logger.LogDebug($"Done {ts} {timestamp}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"{ex}");
                        }
                    }

                }
                Logger.LogDebug($"Sleep {DesiredProperties.Interval} sec");
                await Task.Delay(TimeSpan.FromSeconds(DesiredProperties.Interval)).ConfigureAwait(false);
            }
        }
    }
}
