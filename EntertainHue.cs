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
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using Q42.HueApi;
using Q42.HueApi.Models.Groups;


namespace EntertainHue
{



    class EntertainHue
    {
        LocalHueClient client;
        const string applicationName = "EntertainHue";
        const string clientfile = "client.json";
        string hostName = Dns.GetHostName();

        private Socket socket;
        private UdpTransport udp;
        private DtlsTransport dtlsTransport;

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


            await Verbose("Enabling streaming on group");

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;



            await client.SetStreamingAsync(group.Id, true);
            var ss = await SubscribeEventStream(hue.Ip, clientdata.Value.Username, token);
            await client.SetStreamingAsync(group.Id, true);
            //await PutToSubscribeEventStream(hue.Ip, clientdata.Value.Username, token);

            string guid = "asd";
            while (true)
            {
                var s = await ss.GetNext(token);
                await Verbose(s);

                try
                {
                    var obj = JArray.Parse(s);
                    guid = (string)obj[0]["data"][0]["id"];
                    break;
                }
                catch (Exception e)
                {
                    await Verbose(e.ToString());
                }
            }

            await Connect(clientdata.Value.StreamingClientKey, clientdata.Value.Username, hue.Ip);

            var random = new Random();


            Task t1 = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await SendState(guid, group.Lights.Select(l => ((byte)int.Parse(l), (byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255))));
                    try
                    {
                        await Task.Delay(50, token);
                    }
                    catch { }
                }

                await SendState(guid, group.Lights.Select(l => ((byte)int.Parse(l), (byte)255, (byte)255, (byte)255)));

            }, token);

            Task t2 = Task.Run(() =>
            {
                Console.ReadKey(true);
                cancellationTokenSource.Cancel();
            });

            await Task.WhenAll(t1, t2);


            await client.SetStreamingAsync(group.Id, false);

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

        async Task Connect(string clientkey, string appkey, IPAddress ip)
        {
            byte[] psk = FromHex(clientkey);
            BasicTlsPskIdentity pskIdentity = new BasicTlsPskIdentity(appkey, psk);

            var dtlsClient = new DtlsClient(null!, pskIdentity);

            DtlsClientProtocol clientProtocol = new DtlsClientProtocol(new SecureRandom());

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            await socket.ConnectAsync(ip, 2100).ConfigureAwait(false);
            udp = new UdpTransport(socket);

            dtlsTransport = clientProtocol.Connect(dtlsClient, udp);
        }

        internal class StreamSubscription
        {
            private readonly StreamReader streamReader;

            public StreamSubscription(StreamReader streamReader)
            {
                this.streamReader = streamReader;
            }
            public async Task<string> GetNext(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    Memory<char> buff = new Memory<char>(new char[1024]);
                    var bytes = await streamReader.ReadAsync(buff, token);
                    if (bytes > 0)
                    {
                        return MakeString(buff, bytes);
                    }
                }
                return null;

                string MakeString(Memory<char> buff, int bytes)
                {
                    Span<char> newspan = buff.Span.Slice(0, bytes);
                    string s = new string(newspan);
                    return s;
                }
            }
        }

        async Task PutToSubscribeEventStream(IPAddress ip, string username, CancellationToken cancellation)
        {
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            HttpClient c = new HttpClient(handler);
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Put, $"https://{ip}/eventstream/clip/v2"))
            {
                requestMessage.Headers.Add("ssl", "False");
                requestMessage.Headers.Add("hue-application-key", username);

                var response = await c.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();
                return;
            }
        }

        async Task<StreamSubscription> SubscribeEventStream(IPAddress ip, string username, CancellationToken cancellation)
        {
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            HttpClient c = new HttpClient(handler);
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"https://{ip}/eventstream/clip/v2"))
            {
                requestMessage.Headers.Add("ssl", "False");
                requestMessage.Headers.Add("hue-application-key", username);

                var response = await c.SendAsync(requestMessage);
                //response.EnsureSuccessStatusCode();


                var stream = await response.Content.ReadAsStreamAsync(cancellation);
                var streamReader = new StreamReader(stream);
                return new StreamSubscription(streamReader);
            }

        }

        private static byte[] FromHex(string hex)
        {
            hex = hex.Replace("-", "");
            byte[] raw = new byte[hex.Length / 2];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return raw;
        }

        private static readonly List<byte> protocolName = Encoding.ASCII.GetBytes(new char[] { 'H', 'u', 'e', 'S', 't', 'r', 'e', 'a', 'm' }).ToList();
        async Task SendState(string guid, IEnumerable<(byte light, byte r, byte g, byte b)> lights)
        {
            List<byte> result = new List<byte>();

            result.AddRange(protocolName);

            result.AddRange(new byte[] { //protocol
                0x02, 0x00, //version 2.0
                0x00, //sequence number ignored
                0x00, 0x00, //reserved
                0x00, //color mode RGB
                0x00, //reserved
                // '<group_id_string>', // Char data of group id
                //0x02, //light 1
                //0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                //0x02, //light 2
                //0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                //0x02, //light 3
                //0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                });

            result.AddRange(Encoding.ASCII.GetBytes(guid));

            foreach (var light in lights)
            {
                result.AddRange(new byte[] { light.light, light.r, light.r, light.g, light.g, light.b, light.b });
            };

            var array = result.ToArray();

            await Verbose(Convert.ToHexString(array));

            Send(array);
        }

        protected virtual int Send(byte[] buffer)
        {
            dtlsTransport.Send(buffer, 0, buffer.Length);

            return buffer.Length;
        }

    }
}