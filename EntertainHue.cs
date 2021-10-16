using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Q42.HueApi;
using Q42.HueApi.Interfaces;

class EntertainHue
{
    LocalHueClient client;
    const string applicationName = "EntertainHue";
    const string clientfile = "client.json";
    string hostName = Dns.GetHostName();

    async Task<HueFoundResponse> FindHueAsync(CancellationToken token)
    {
        var message = "M-SEARCH * HTTP/1.1\r\n" +
            "HOST:239.255.255.250:1900\r\n" +
            "ST:upnp:rootdevice\r\n" +
            "MX:2\r\n" +
            "MAN:\"ssdp:discover\"\r\n\r\n";

        var mcastip = IPAddress.Parse("239.255.255.250");
        using var client = new UdpClient();
        var bindendpoint = new IPEndPoint(IPAddress.Any, 65507);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(bindendpoint);
        client.JoinMulticastGroup(mcastip);
        var bytes = Encoding.UTF8.GetBytes(message);

        var endpoint = new IPEndPoint(mcastip, 1900);
        var length = await client.SendAsync(bytes, bytes.Length, endpoint);
        var rowsplitter = new Regex("\r\n|\r|\n");
        var keyvaluesplitter = new Regex("^([^\\:]*):\\s*(.*)$");
        while (!token.IsCancellationRequested)
        {
            try
            {
                var data = await client.ReceiveAsync().WithCancellation(token);
                var str = Encoding.UTF8.GetString(data.Buffer, 0, data.Buffer.Length);
                var lines = rowsplitter.Split(str);
                var dict = new Dictionary<string, string>();
                foreach (var line in lines)
                {
                    var match = keyvaluesplitter.Match(line);
                    if (match.Success)
                    {
                        var key = match.Groups[1].Value;
                        var value = match.Groups[2].Value;
                        dict.Add(key, value);
                    }
                }

                if (dict.ContainsKey("hue-bridgeid"))
                {
                    return new HueFoundResponse() { Ip = data.RemoteEndPoint.Address, Port = 0, Headers = dict, Found = true };
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        return new HueFoundResponse() { Found = false };
    }

    async Task<HueFoundResponse> TryFindHueAsync(int timeout)
    {
        var cancel = new CancellationTokenSource();
        var findHueTask = FindHueAsync(cancel.Token);
        var timeOutTask = Task.Delay(timeout, cancel.Token);
        var result = await Task.WhenAny(new[] { findHueTask, timeOutTask });
        if (result == timeOutTask)
        {
            cancel.Cancel();
        }
        return await findHueTask;
    }

    void Verbose(params string[] s)
    {
        var line = string.Join(" ", s);
        Console.WriteLine(line);
    }

    async Task<bool> CheckVersion()
    {
        Verbose("Requesting bridge information...");
        var hostname = Dns.GetHostName();


        var config = await client.GetConfigAsync();
        if (config.ApiVersion.CompareTo("1.22") < 0)
        {
            Verbose("Hue apiversion not 1.22 or above.");
            return false;
        }
        Verbose($"Api version good {config.ApiVersion}...");
        return true;
    }

    struct ClientData
    {
        public string Username;
        public string StreamingClientKey;
    };
    async Task<ClientData?> RegisterApp()
    {
        int triesleft = 10;
        while (triesleft > 0)
        {
            try
            {
                var mess = await client.RegisterAsync(applicationName, hostName, true);

                var clientdata = new ClientData()
                {
                    Username = mess.Username,
                    StreamingClientKey = mess.StreamingClientKey
                };

                return clientdata;

            }
            catch (Exception e)
            {
                Verbose(e.Message);
            }
            await Task.Delay(5000);
        }
        Verbose("No client registered");
        return null;
    }

    public async Task<int> Run(string[] args)
    {
        Verbose("Finding hue bridge...");
        var hue = await TryFindHueAsync(5000);
        if (!hue.Found)
        {
            Verbose($"No Bridge found");
            return -1;
        }

        Verbose($"Bridge found on {hue.Ip}");

        client = new LocalHueClient(hue.Ip.ToString());
        if (!await CheckVersion()) return -2;

        var clientdata = await ReadClientFile();
        if (clientdata is null)
        {
            clientdata = await RegisterApp();
            if (clientdata is null) return -3;
            await SaveClientFile(clientdata.Value);
        }

        return 0;
    }

    private async Task SaveClientFile(ClientData clientData)
    {
        var text = JsonConvert.SerializeObject(clientData);
        await File.WriteAllTextAsync(clientfile, text, Encoding.UTF8);
    }

    async Task<ClientData?> ReadClientFile()
    {
        Verbose($"Checking for {clientfile}");
        if (!File.Exists(clientfile))
            return null;

        var json = await File.ReadAllTextAsync(clientfile, Encoding.UTF8);
        var clientdata = JsonConvert.DeserializeObject<ClientData>(json);
        Verbose("Client data found", clientdata.ToString());
        return clientdata;
    }

}
