namespace SampleModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using bluez.DBus;
    using Tmds.DBus;
    using System.Collections.Generic;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static int counter;

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

            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, ioTHubModuleClient);

            try
            {
                var adapter = Connection.System.CreateProxy<IAdapter1>("org.bluez", "/org/bluez/hci0");
                if (adapter == null)
                {
                    Console.WriteLine($"Bluetooth adapter not found.");
                }
                var manager = Connection.System.CreateProxy<IObjectManager>("org.bluez", "/");
                if (manager == null)
                {
                    Console.WriteLine($"Bluetooth object manager not found.");
                }
                var iaddDisp = await manager.WatchInterfacesAddedAsync(
                    async args =>
                    {
                        Console.WriteLine("WatchInterfacesAddedAsync");
                        foreach (var item in args.interfaces)
                        {
                            Console.WriteLine($"{args.@object}: {item.Key}");
                            if (item.Key == "org.bluez.Device1")
                            {
                                var device = Connection.System.CreateProxy<IDevice1>("org.bluez", args.@object);
                                var props = await device.GetAllAsync();
                                Console.WriteLine($"  Name:{props.Name}");
                                Console.WriteLine($"  Address:{props.Address}");
                                Console.WriteLine($"  RSSI:{props.RSSI}");
                            }
                        }
                    }
                );
                var irmDisp = await manager.WatchInterfacesRemovedAsync(
                    args =>
                    {
                        Console.WriteLine("WatchInterfacesRemovedAsync");
                        foreach (var item in args.interfaces)
                        {
                            Console.WriteLine($"{item}");
                        }
                    }
                );
                await adapter.StartDiscoveryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception {ex}");
            }
            SendSimulationData(ioTHubModuleClient);
        }

        static Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            ModuleClient moduleClient = userContext as ModuleClient;
            Console.WriteLine($"{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()} Desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));
            return Task.CompletedTask;
        }

        static async Task SendSimulationData(ModuleClient moduleClient)
        {
            while (true)
            {
                try
                {
                    string messageString = "{\"message\": \"Hello World\"}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(messageString);
                    Message message = new Message(messageBytes);
                    message.ContentEncoding = "utf-8";
                    message.ContentType = "application/json";
                    await moduleClient.SendEventAsync("helloOutput", message);
                    Console.WriteLine($"\t{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToLongTimeString()}: Hello World");
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected Exception {ex.Message}");
                    Console.WriteLine($"\t{ex.ToString()}");
                }
            }
        }
    }
}
