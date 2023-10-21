using System;
using Symbol.RFID3;
using Serilog;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace UpsDispatchService
{
    public class Program
    {
        public static void Main(string[] args) =>
            new Program().MainInstance(args).GetAwaiter().GetResult();

        public async Task MainInstance(string[] args)
        {
            InitLogService();
            string hostIp = "0.0.0.0";
            if (args.Length == 0)
            {
                Log.Error("No value passed for 'host_ip' parameter...!");
                Environment.Exit(1);
            }else
            { 
                hostIp = args[0];
            }
            RfidReader rfidReader = new(hostIp);
            rfidReader.StartPolling();

            Console.CancelKeyPress += delegate
            {
                rfidReader.Dispose();
            };
            await Task.Delay(Timeout.Infinite);
        }

        public void InitLogService()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            Log.Information("Logging services started...");
        }
    }

    public class RfidReader : IDisposable
    {
        private readonly string _host;
        private readonly uint _port;
        private RFIDReader _rfidReader;
        private int count = 0;
        private readonly HttpClient _httpClient;
        private List<string> antenna1ReadTags = new List<string>();
        private List<string> antenna2ReadTags = new List<string>();

        public RfidReader(string host, uint port = 0)
        {
            _host = host;
            _port = port;
            _httpClient = new HttpClient();
        }

        public void Connect()
        {
            _rfidReader = new RFIDReader(_host, _port, 0);

            try
            {
                _rfidReader.Connect();
                Log.Information($"Connected to zebra hardware at: {_host}");
            } catch (Exception ex)
            {
                Log.Error($"Can't connect to zebra hardware. \n Details: {ex}!");
                Environment.Exit(1);
            }
        }

        public void StartPolling()
        {
            Connect();
            _rfidReader.Actions.Inventory.Perform();
            _rfidReader.Events.ReadNotify += Rfid_DataRecieved;
            Log.Information("Rfid initiated...");
            Log.Information("Inventory operation started...");
        }

        private async void Rfid_DataRecieved(object sender, Events.ReadEventArgs e)
        {
            ushort antenna = e.ReadEventData.TagData.AntennaID;
            Log.Debug($"Antenna Id: {antenna}");
            string recievedTag = e.ReadEventData.TagData.TagID;
            Log.Debug($"Tag id: {recievedTag}");


            // Antenna 1 Read & Update
            if (antenna == 1 && !antenna1ReadTags.Contains(recievedTag))
            {
                Log.Information($"Antenna 1 Triggered - {recievedTag}");
                antenna1ReadTags.Add(recievedTag);
                await UpdateDispatched(recievedTag,
                    "Recieved at Atlanta Hub", "DISPATCHED_2");
            }

            //Antenna 2 Read & Update
            if (antenna == 2 && !antenna2ReadTags.Contains(recievedTag))
            {
                Log.Information($"Antenna 2 triggered - {recievedTag}");
                antenna2ReadTags.Add(recievedTag);
                await UpdateDispatched(recievedTag,
                    "Recieved at Chennai Hub, Dispatched to Atlanta Hub", "DISPATCHED_1");
            }
        }

        public async Task UpdateDispatched
            (string TagId, string CurrentHub, string Status)
        {
            Log.Information("Starting push payload to API");
            string requestUri = 
                $"http://172.22.81.182:8080/rfid/updateCurrentHub?rfid={TagId}&current_hub={CurrentHub}&status={Status}";


            try
            {
                HttpResponseMessage message = await _httpClient.PostAsync(requestUri, null);
                if (message.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine(await message.Content.ReadAsStringAsync());
                    Log.Information("Payload sent to server successfully");
                }
                else
                {
                    Log.Error($"API Returned error. Code: {message.StatusCode}");
                }
            } catch (Exception ex)
            {
                Log.Error($"Error connecting to API. Code: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Log.Information("Cleaning up resources...");
            _httpClient.Dispose();
            _rfidReader.Actions.Inventory.Stop();
            _rfidReader.Dispose();
            Log.Information("Exiting...");
            Environment.Exit(1);
        }
    }
}
