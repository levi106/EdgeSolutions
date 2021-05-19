namespace SampleModule
{
    using System;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using System.Net;

    class Program
    {
        static volatile DesiredPropertiesData DesiredProperties;
        static volatile int Count = 0;

        static void Main(string[] args)
        {
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
            // MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // ITransportSettings[] settings = { mqttSetting };
            AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            ITransportSettings[] settings = { amqpSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            Twin moduleTwin = await ioTHubModuleClient.GetTwinAsync();
            TwinCollection moduleTwinCollection = moduleTwin.Properties.Desired;
            DesiredProperties = new DesiredPropertiesData(moduleTwinCollection);
            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, ioTHubModuleClient);
            await ioTHubModuleClient.SetMethodHandlerAsync("countup", CountupMethod, null);
            await ioTHubModuleClient.SetMethodHandlerAsync("restart", RestartMethod, ioTHubModuleClient);
        }

        static Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            ModuleClient moduleClient = userContext as ModuleClient;
            Console.WriteLine($"{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()} Desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));
            DesiredProperties = new DesiredPropertiesData(desiredProperties);
            Restart(moduleClient);
            return Task.CompletedTask;
        }

        static Task<MethodResponse> CountupMethod(MethodRequest request, object userContext)
        {
            var response = new MethodResponse((int)HttpStatusCode.OK);
            Console.WriteLine($"{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()} Received countup command via direct method invocation");
            Count += 1;
            Console.WriteLine($"Current count is {Count}");
            return Task.FromResult(response);
        }

        static void Restart(ModuleClient moduleClient)
        {
            Task.Run(async () =>
            {
                Console.WriteLine("CloseAsync");
                await moduleClient.CloseAsync();
                Console.WriteLine("Dispose");
                moduleClient.Dispose();
                Console.WriteLine("Init");
                await Init();
                Console.WriteLine("Done");
            });
        }

        static Task<MethodResponse> RestartMethod(MethodRequest request, object userContext)
        {
            ModuleClient moduleClient = userContext as ModuleClient;
            var response = new MethodResponse((int)HttpStatusCode.OK);
            Console.WriteLine($"{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()} Received restart command via direct method invocation");
            Restart(moduleClient);
            return Task.FromResult(response);
        }
    }
}
