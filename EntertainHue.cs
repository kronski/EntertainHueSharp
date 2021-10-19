using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Q42.HueApi;
using Q42.HueApi.ColorConverters;
using Q42.HueApi.Interfaces;
using Q42.HueApi.Models.Groups;
using Q42.HueApi.Streaming;
using Q42.HueApi.Streaming.Models;

class EntertainHue
{
    LocalHueClient client;
    const string applicationName = "EntertainHue";
    const string clientfile = "client.json";
    string hostName = Dns.GetHostName();

    Options options;
    class Options
    {
        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
        public bool Verbose { get; set; }
        [Option('g', "groupid", Required = false, HelpText = "Specify entertainment groupid.")]
        public string GroupId { get; set; }
        [Option('i', "ip", Required = false, HelpText = "Hue ip.")]
        public string IpNumber { get; set; }
    }

    async Task<HueFindResponse> FindHueAsync(CancellationToken token)
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
        var rowsplitter = new System.Text.RegularExpressions.Regex("\r\n|\r|\n");
        var keyvaluesplitter = new System.Text.RegularExpressions.Regex("^([^\\:]*):\\s*(.*)$");
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
                    return new HueFindResponse() { Ip = data.RemoteEndPoint.Address, Port = 0, Headers = dict, Found = true };
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        return new HueFindResponse() { Found = false };
    }

    async Task<HueFindResponse> TryFindHueAsync(int timeout)
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

    async Task Verbose(params string[] s)
    {
        if (!options.Verbose) return;
        var line = string.Join(" ", s);
        await Console.Out.WriteLineAsync(line);
    }

    async Task Error(params string[] s)
    {
        var line = string.Join(" ", s);
        await Console.Error.WriteLineAsync(line);
    }

    async Task<bool> CheckVersion()
    {
        await Verbose("Requesting bridge information...");
        var hostname = Dns.GetHostName();


        var config = await client.GetConfigAsync();
        if (config.ApiVersion.CompareTo("1.22") < 0)
        {
            await Verbose("Hue apiversion not 1.22 or above.");
            return false;
        }
        await Verbose($"Api version good {config.ApiVersion}...");
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
                await Verbose(e.Message);
            }
            await Task.Delay(5000);
        }
        await Verbose("No client registered");
        return null;
    }

    public async Task<int> Run(string[] args)
    {
        await Parser.Default.ParseArguments<Options>(args)
            .WithParsed<Options>(o =>
            {
                options = o;
            }).WithNotParsedAsync(async (errors) =>
            {
                foreach (var e in errors)
                {
                    await Error(e.ToString());
                }
            });
        if (options is null)
            return -1;

        HueFindResponse hue;
        if (options.IpNumber is null)
        {
            await Verbose("Finding hue bridge...");
            hue = await TryFindHueAsync(5000);
            if (!hue.Found)
            {
                await Error($"No Bridge found");
                return -1;
            }
            await Verbose($"Bridge found on {hue.Ip}");
        }
        else
        {
            hue = new HueFindResponse()
            {
                Found = true,
                Ip = IPAddress.Parse(options.IpNumber),
                Port = 0
            };
            await Verbose($"Using bridge {hue.Ip}");
        }


        client = new LocalHueClient(hue.Ip.ToString());
        if (!await CheckVersion()) return -2;

        var clientdata = await ReadClientFile();
        if (clientdata is null)
        {
            clientdata = await RegisterApp();
            if (clientdata is null) return -3;
            await SaveClientFile(clientdata.Value);
        }

        client.Initialize(clientdata.Value.Username);

        await Verbose("Checking for entertainment groups");
        var groups = (await client.GetEntertainmentGroups()).Where(g => options.GroupId is null || options.GroupId == g.Id);
        Group group;
        switch (groups.Count())
        {
            case 0:
                await Error("No entertainment groups found");
                return -3;
            case 1:
                group = groups.First();
                await Verbose("Using group", group.Id);
                break;
            default:
                await Error("Multiple entertainment groups found, specify which with --groupid");
                foreach (var g in groups)
                {
                    await Error($"  {g.Id} = {g.Name}");
                }
                return -3;
        }


        using var streamingHueClient = new StreamingHueClient(hue.Ip.ToString(), clientdata.Value.Username, clientdata.Value.StreamingClientKey);

        await Verbose("Enabling streaming on group");

        var entGroup = new StreamingGroup(group.Locations);

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = cancellationTokenSource.Token;

        //await streamingHueClient.LocalHueClient.SetStreamingAsync(group.Id, false);
        await streamingHueClient.LocalHueClient.SetStreamingAsync(group.Id, true);
        await streamingHueClient.Connect(group.Id);

        var random = new Random();

        Task t1 = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await UpdateHueState(new RGBColor(random.NextDouble(), random.NextDouble(), random.NextDouble()));
                try
                {
                    await Task.Delay(50, token);
                }
                catch { }
            }

            await UpdateHueState(new RGBColor(1.0, 1.0, 1.0));

            async Task UpdateHueState(RGBColor color)
            {
                await Verbose($"{color.R},{color.G},{color.B}");
                foreach (var l in entGroup)
                {
                    l.State.SetBrightness(1);
                    l.State.SetRGBColor(color);
                }
                streamingHueClient.ManualUpdate(entGroup);
            }
        }, token);

        Task t2 = Task.Run(() =>
        {
            Console.ReadKey(true);
            cancellationTokenSource.Cancel();
        });

        await Task.WhenAll(t1, t2);


        await streamingHueClient.LocalHueClient.SetStreamingAsync(group.Id, false);

        return 0;
    }

    private async Task SaveClientFile(ClientData clientData)
    {
        var text = JsonConvert.SerializeObject(clientData);
        await File.WriteAllTextAsync(clientfile, text, Encoding.UTF8);
    }

    async Task<ClientData?> ReadClientFile()
    {
        await Verbose($"Checking for {clientfile}");
        if (!File.Exists(clientfile))
            return null;

        var json = await File.ReadAllTextAsync(clientfile, Encoding.UTF8);
        var clientdata = JsonConvert.DeserializeObject<ClientData>(json);
        await Verbose("Client data found", clientdata.ToString());
        return clientdata;
    }

}
