using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntertainHue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using Q42.HueApi;
using Q42.HueApi.Models.Groups;

public class HueService
{
    static HueService current = new HueService();
    public static HueService Current => current;

    private LocalHueClient client;
    private const string clientfile = "client.json";
    private const string applicationName = "EntertainHue";
    private string hostName = Dns.GetHostName();
    private Socket socket;
    private UdpTransport udp;
    private DtlsTransport dtlsTransport;
    private FindHueResponse hue;
    private Group group;
    private ClientData? clientdata;
    private string guid;

    private readonly List<(byte light, byte r, byte g, byte b)> lightstate;
    private SemaphoreSlim stateSemaphore;

    struct ClientData
    {
        public string Username;
        public string StreamingClientKey;
    };

    public HueService()
    {
        stateSemaphore = new SemaphoreSlim(1);
        lightstate = InitLightState();
    }

    private List<(byte light, byte r, byte g, byte b)> InitLightState()
    {
        return GetLights().Select(l => ((byte)int.Parse(l), (byte)180, (byte)180, (byte)180)).ToList();
    }

    public async Task<FindHueResponse> LocateHue()
    {
        FindHueResponse hue;
        if (Options.Current.IpNumber is null)
        {
            await ConsoleEx.Verbose("Finding hue bridge...");
            hue = await FindHue.TryFindHueAsync(5000);
            if (!hue.Found)
                await ConsoleEx.Error($"No Bridge found");
            else
                await ConsoleEx.Verbose($"Bridge found on {hue.Ip}");
        }
        else
        {
            hue = new FindHueResponse()
            {
                Found = true,
                Ip = IPAddress.Parse(Options.Current.IpNumber),
                Port = 0
            };
            await ConsoleEx.Verbose($"Using bridge {hue.Ip}");
        }
        return hue;
    }

    internal async Task RunStreaming(CancellationToken token)
    {
        await CheckConnect();

        var random = new Random();

        while (!token.IsCancellationRequested)
        {
            await SendState();
            try
            {
                await Task.Delay(50, token);
            }
            catch { }
        }

        await SetLightState(InitLightState());
        await SendState();
        await client.SetStreamingAsync(group.Id, false);
    }

    private async Task SetLightState(IEnumerable<(byte light, byte r, byte g, byte b)> list)
    {
        try
        {
            await stateSemaphore.WaitAsync();
            lightstate.Clear();
            lightstate.AddRange(list);
        }
        finally
        {
            stateSemaphore.Release();
        }
    }

    async Task<bool> CheckVersion()
    {
        await ConsoleEx.Verbose("Requesting bridge information...");
        var hostname = Dns.GetHostName();

        var config = await client.GetConfigAsync();
        if (config.ApiVersion.CompareTo("1.22") < 0)
        {
            await ConsoleEx.Verbose("Hue apiversion not 1.22 or above.");
            return false;
        }
        await ConsoleEx.Verbose($"Api version good {config.ApiVersion}...");
        return true;
    }

    internal async Task RunRandomLight(CancellationToken token)
    {
        var random = new Random();

        IEnumerable<string> lights = GetLights();
        while (!token.IsCancellationRequested)
        {
            await SetLightState(lights.Select(l => ((byte)int.Parse(l), (byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255))));
            await Task.Delay(50);
        }

    }

    private static IEnumerable<string> GetLights()
    {
        return Enumerable.Range(0, 9).Select(x => x.ToString());
    }

    private async Task CheckConnect()
    {
        if (socket == null)
            await Connect(clientdata.Value.StreamingClientKey, clientdata.Value.Username, hue.Ip);
    }

    internal async Task<int> InitEntertainmentGroup()
    {
        await ConsoleEx.Verbose("Checking for entertainment groups");
        var groups = (await client.GetEntertainmentGroups()).Where(g => Options.Current.GroupId is null || Options.Current.GroupId == g.Id);
        switch (groups.Count())
        {
            case 0:
                await ConsoleEx.Error("No entertainment groups found");
                return -3;
            case 1:
                group = groups.First();
                await ConsoleEx.Verbose("Using group", group.Id);
                break;
            default:
                await ConsoleEx.Error("Multiple entertainment groups found, specify which with --groupid");
                foreach (var g in groups)
                {
                    await ConsoleEx.Error($"  {g.Id} = {g.Name}");
                }
                return -3;
        }

        await ConsoleEx.Verbose("Enabling streaming on group");

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(20000);
        CancellationToken token = cancellationTokenSource.Token;

        await client.SetStreamingAsync(group.Id, true);
        using (var ss = new StreamSubscription())
        {
            await ss.SubscribeEventStream(hue.Ip, clientdata.Value.Username, token);
            await client.SetStreamingAsync(group.Id, true);

            guid = await ss.GetGuidAsync(token);
        }
        if (guid == null)
        {
            await ConsoleEx.Error("Unable to retrive guid");
            return -4;
        }
        await ConsoleEx.Verbose(guid);
        return 0;
    }

    public async Task<int> FindAndInit()
    {
        hue = await LocateHue();
        if (!hue.Found)
            return -1;

        client = new LocalHueClient(hue.Ip.ToString());
        if (!await CheckVersion())
            return -2;

        clientdata = await ReadClientFile();
        if (clientdata is null)
        {
            clientdata = await RegisterApp();
            if (clientdata is null) return -3;
            await SaveClientFile(clientdata.Value);
        }

        client.Initialize(clientdata.Value.Username);
        return 0;
    }

    async Task<ClientData?> ReadClientFile()
    {
        await ConsoleEx.Verbose($"Checking for {clientfile}");
        if (!File.Exists(clientfile))
            return null;

        var json = await File.ReadAllTextAsync(clientfile, Encoding.UTF8);
        var clientdata = JsonConvert.DeserializeObject<ClientData>(json);
        await ConsoleEx.Verbose("Client data found", clientdata.ToString());
        return clientdata;
    }

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
                await ConsoleEx.Verbose(e.Message);
            }
            await Task.Delay(5000);
        }
        await ConsoleEx.Verbose("No client registered");
        return null;
    }

    private async Task SaveClientFile(ClientData clientData)
    {
        var text = JsonConvert.SerializeObject(clientData);
        await File.WriteAllTextAsync(clientfile, text, Encoding.UTF8);
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

    internal class StreamSubscription : IDisposable
    {
        private StreamReader streamReader;
        private HttpClient client;

        public StreamSubscription()
        {
            client = new HttpClient(new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        public void Dispose()
        {
            client?.Dispose();
            streamReader?.Dispose();
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

        public async Task SubscribeEventStream(IPAddress ip, string username, CancellationToken cancellation)
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"https://{ip}/eventstream/clip/v2"))
            {
                requestMessage.Headers.Add("ssl", "False");
                requestMessage.Headers.Add("hue-application-key", username);

                var response = await client.SendAsync(requestMessage);
                //response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(cancellation);
                streamReader = new StreamReader(stream);
                return;
            }

        }

        public async Task<string> GetGuidAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var s = await GetNext(token);
                if (s == null) return null;

                var obj = JToken.Parse(s);
                if (obj is JArray array && array.Count > 0 &&
                    array[0] is JObject array0 && array0.TryGetValue("data", StringComparison.InvariantCulture, out var data) &&
                    data is JArray dataarray && dataarray.Count > 0 &&
                    dataarray[0] is JObject idobject && idobject.TryGetValue("id", StringComparison.InvariantCulture, out var id))
                {
                    return (string)id;
                }
            }
            return null;
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
    async Task SendState()
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

        await stateSemaphore.WaitAsync();
        try
        {
            foreach (var light in lightstate)
            {
                result.AddRange(new byte[] { light.light, light.r, light.r, light.g, light.g, light.b, light.b });
            };
        }
        finally
        {
            stateSemaphore.Release();
        }

        var array = result.ToArray();

        Send(array);
    }

    protected virtual int Send(byte[] buffer)
    {
        dtlsTransport.Send(buffer, 0, buffer.Length);

        return buffer.Length;
    }

    internal async Task TurnOnSegment(byte segment)
    {
        await CheckConnect();

        var lights = GetLights();
        await SetLightState(lights.Select(l =>
       {
           byte light = (byte)int.Parse(l);
           byte state = (light == segment) ? (byte)255 : (byte)0;
           return (light, state, state, state);
       }));
    }

    public void CleanUp()
    {
        if (socket is not null)
            socket.Close();

    }

}