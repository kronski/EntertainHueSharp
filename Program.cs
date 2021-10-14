using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

void Verbose(string s)
{
    Console.WriteLine(s);
}

async Task<int> Run(string[] args)
{
    Verbose("Finding hue bridge...");
    var hue = await TryFindHueAsync(5000);
    if (hue.Found)
    {
        Verbose($"Bridge found on {hue.Ip}");
    }
    else
    {
        Verbose($"No Bridge found");
        return -1;
    }
    return 0;
}

return await Run(args);