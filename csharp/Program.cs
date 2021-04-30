namespace NotMyFaultModule
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Prometheus;

    class Program
    {
        static int counter;

        private static readonly Counter CustomCounterMetric =
            Metrics.CreateCounter("notmyfault_counter_total", "Cumulative counter, increment only every 500 msec",  labelNames: new[] { "edge_device_id", "instance_id", "iothub_name", "module_id" } );

        private static readonly Gauge CustomGaugeMetric = Metrics
            .CreateGauge("notmyfault_gauge_current", "Gauges can have any numeric value and change arbitrarily, random number every sec", labelNames: new[] { "edge_device_id", "instance_id", "iothub_name", "module_id" });

        private static readonly Summary CustomSummaryMetric = Metrics
            .CreateSummary("notmyfault_summary_bytes", "Summaries track the trends in events over time (10 minutes by default).", labelNames: new[] { "edge_device_id", "instance_id", "iothub_name", "module_id" });

        static void MyHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Console.WriteLine("MyHandler caught : " + e.Message);
            Console.WriteLine("Runtime terminating: {0}", args.IsTerminating);
        }

        static void Main(string[] args)
        {
            var server = new MetricServer(9600);
            server.Start();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(MyHandler);

            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            string deviceId = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
            string instanceNumber = Guid.NewGuid().ToString();
            string iothubHostname = Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");
            string moduleId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");


            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    Task.Delay(500);
                    CustomCounterMetric.WithLabels(deviceId, instanceNumber, iothubHostname, moduleId).Inc();
                    //if (CustomCounterMetric.Value % 50 == 0)
                    //{
                    //    Console.WriteLine($"CustomCounterMetric is now {CustomCounterMetric.Value}");
                    //}
                }
                Console.WriteLine($"Exiting from metric loop");
            });
            Random r = new();

            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    Task.Delay(1000);
                    CustomGaugeMetric.WithLabels(deviceId, instanceNumber, iothubHostname, moduleId).Set(r.Next(0, 500));
                }
            });

            Random randomSummary = new();
            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    Task.Delay(1000);
                    CustomSummaryMetric.WithLabels(deviceId, instanceNumber, iothubHostname, moduleId).Observe(randomSummary.NextDouble() * 1024 * 1024);
                }
            });

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

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);
            await ioTHubModuleClient.SetMethodHandlerAsync("terminate", FaultMethodInvoked, ioTHubModuleClient);
        }

        private async static Task<MethodResponse> FaultMethodInvoked(MethodRequest methodRequest, object userContext)
        {
            Task.Run(() => FaultMethodImplementation());
            Console.WriteLine("method invoked");
            return new MethodResponse(200);
        }

        private async static void FaultMethodImplementation()
        {
            await Task.Delay(5000);
            System.Environment.Exit(-1);
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                using (var pipeMessage = new Message(messageBytes))
                {
                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }
                    await moduleClient.SendEventAsync("output1", pipeMessage);

                    Console.WriteLine("Received message sent");
                }
            }
            return MessageResponse.Completed;
        }
    }
}
