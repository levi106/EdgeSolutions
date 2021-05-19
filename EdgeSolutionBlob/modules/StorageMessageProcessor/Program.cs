namespace StorageMessageProcessor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
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

    class Program
    {
        public class Telemetry
        {
            public string Path { get; set; }
        }

        static ILogger<Program> Logger;
        static TelemetryClient TelemetryClient;
        static BlobContainerClient ContainerClient;
        static string ModuleName;

        static void Main(string[] args)
        {
            ModuleName = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
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

            Logger = serviceProvider.GetRequiredService<ILogger<StorageMessageProcessor.Program>>();
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
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            Logger.LogDebug($"{connectionString}");
            ContainerClient = new BlobContainerClient(connectionString, "samplecontainer");
            try
            {
                await ContainerClient.CreateIfNotExistsAsync();
            }
            catch (RequestFailedException ex)
            {
                Logger.LogError($"{ex}");
            }

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

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
            Logger.LogDebug($"Received message: Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                try
                {
                    var moduleClient = userContext as ModuleClient;

                    var telemetry = JsonConvert.DeserializeObject<Telemetry>(messageString);
                    string filePath = telemetry.Path;
                    Logger.LogDebug($"Download {filePath}");

                    BlobClient blobClient = ContainerClient.GetBlobClient(filePath);

                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    BlobDownloadInfo response = await blobClient.DownloadAsync();
                    using (BinaryReader reader = new BinaryReader(response.Content))
                    {
                        byte[] data = reader.ReadBytes((int)response.ContentLength);
                    }
                    sw.Stop();
                    Logger.LogDebug($"Done {filePath} {sw.Elapsed.TotalMilliseconds} {response.ContentLength}");

                    TimeSpan ts = sw.Elapsed;
                    var perf = new MetricTelemetry();
                    perf.Name = ModuleName;
                    perf.Sum = ts.TotalMilliseconds;
                    TelemetryClient.TrackMetric(perf);

                    using (var pipeMessage = new Message(messageBytes))
                    {
                        foreach (var prop in message.Properties)
                        {
                            pipeMessage.Properties.Add(prop.Key, prop.Value);
                            Logger.LogDebug($"{prop.Key}: {prop.Value}");
                        }
                        await moduleClient.SendEventAsync("output1", pipeMessage);

                        Logger.LogDebug("Received message sent");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"{ex}");
                }

            } else {
                Logger.LogDebug("Message is empty");
            }
            return MessageResponse.Completed;
        }
    }
}
