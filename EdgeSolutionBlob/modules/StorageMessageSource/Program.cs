namespace StorageMessageSource
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
        static ILogger<Program> Logger;
        static TelemetryClient TelemetryClient;
        static BlobContainerClient ContainerClient;
        static DesiredPropertiesData DesiredProperties;
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

            Logger = serviceProvider.GetRequiredService<ILogger<StorageMessageSource.Program>>();
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
            Logger.LogInformation("IoT Hub module client initialized.");

            Twin moduleTwin = await ioTHubModuleClient.GetTwinAsync();
            TwinCollection moduleTwinCollection = moduleTwin.Properties.Desired;
            DesiredProperties = new DesiredPropertiesData(moduleTwinCollection);
            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, ioTHubModuleClient);
            await ioTHubModuleClient.SetMethodHandlerAsync("upload", UploadMethod, ioTHubModuleClient);
            await ioTHubModuleClient.SetMethodHandlerAsync("list", ListMethod, ioTHubModuleClient);
#pragma warning disable 4014
            MainLoop(ioTHubModuleClient);
#pragma warning restore 4014
        }

        static async Task<MethodResponse> UploadMethod(MethodRequest request, object userContext)
        {
            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            Logger.LogInformation("UploadMethod was called");
            try
            {
                string sample = "{ \"message\": \"Hello World\" }";
                BlobClient blobClient = ContainerClient.GetBlobClient($"sample_blob_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}");
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sample)))
                {
                    var result = await blobClient.UploadAsync(stream);
                    Logger.LogDebug($"{result.ToString()}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"{ex}");
            }
            return response;
        }

        static async Task<MethodResponse> ListMethod(MethodRequest request, object userContext)
        {
            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            Logger.LogInformation("ListMethod was called");
            try
            {
                List<string> blobs = new List<string>();
                await foreach (var blobItem in ContainerClient.GetBlobsAsync())
                {
                    blobs.Add(blobItem.Name);
                    Logger.LogInformation($"{blobItem.Name}");
                }
                byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blobs));
                return new MethodResponse(json, (int)System.Net.HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                Logger.LogError($"{ex}");
                return new MethodResponse((int)System.Net.HttpStatusCode.OK);
            }
        }

        static Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            Logger.LogInformation($"OnDesiredProperitesUpdate");
            var moduleClient = userContext as ModuleClient;
            DesiredProperties = new DesiredPropertiesData(desiredProperties);
            return Task.CompletedTask;
        }

        static async Task<Stream> CreateMessage()
        {
            MemoryStream stream = new MemoryStream();
            for (int i = 0; i < DesiredProperties.RowCount; i++)
            {
                JObject jo = new JObject();
                jo.Add("timestamp", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                for (int j = 0; j < DesiredProperties.FieldLength; j++)
                {
                    jo.Add($"field{j}", new string('*', DesiredProperties.DataLength));
                }
                await stream.WriteAsync(Encoding.UTF8.GetBytes(jo.ToString()));
            }
            stream.Seek(0, 0);
            return stream;
        }

        static async Task MainLoop(ModuleClient moduleClient)
        {
            Logger.LogInformation("Start MainLoop");
            Stopwatch sw = new Stopwatch();
            while (true)
            {
                try
                {
                    string filePath = $"telemetry_data_{DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")}.json";

                    using (Stream content = await CreateMessage().ConfigureAwait(false))
                    {
                        BlobClient blobClient = ContainerClient.GetBlobClient(filePath);
                        sw.Reset();
                        sw.Start();
                        var response = await blobClient.UploadAsync(content).ConfigureAwait(false);
                        sw.Stop();
                        Logger.LogDebug($"Done {filePath} {sw.Elapsed.TotalMilliseconds} {response.ToString()}");
                    }

                    TimeSpan ts = sw.Elapsed;
                    var perf = new MetricTelemetry();
                    perf.Name = ModuleName;
                    perf.Sum = ts.TotalMilliseconds;
                    TelemetryClient.TrackMetric(perf);

                    string messageString = $"{{\"path\": \"{filePath}\"}}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(messageString);
                    using (Message message = new Message(messageBytes))
                    {
                        await moduleClient.SendEventAsync("output1", message).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Unexpected Exception {ex.Message}");
                    Logger.LogError($"\t{ex.ToString()}");
                }
                Logger.LogDebug($"Sleep {DesiredProperties.Interval} sec");
                await Task.Delay(TimeSpan.FromSeconds(DesiredProperties.Interval)).ConfigureAwait(false);
            }
        }
    }
}
