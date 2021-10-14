using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace EntertainHueSharp
{
    class Program
    {


        async Task<(IPAddress ip, int? port, Dictionary<string,string> headers)> FindHue()
        {
            var message = "M-SEARCH * HTTP/1.1\r\n"+
                "HOST:239.255.255.250:1900\r\n"+
                "ST:upnp:rootdevice\r\n"+
                "MX:2\r\n"+
                "MAN:\"ssdp:discover\"\r\n\r\n";
            
            var mcastip = IPAddress.Parse("239.255.255.250");
            var client = new UdpClient();
            var bindendpoint = new IPEndPoint(IPAddress.Any,1900);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(bindendpoint);
            client.JoinMulticastGroup(mcastip);
            var bytes = Encoding.UTF8.GetBytes(message);

            var endpoint = new IPEndPoint(mcastip,1900);
            var length = await client.SendAsync(bytes, bytes.Length, endpoint);
            Verbose($"Sent {length} bytes");
            var rowsplitter = new Regex("\r\n|\r|\n");
            var keyvaluesplitter = new Regex("^([^\\:]*):\\s*(.*)$");
            while(true) 
            {
                var data = await client.ReceiveAsync();
                
                var str = Encoding.UTF8.GetString(data.Buffer,0, data.Buffer.Length);
                Verbose(str);
                
                var lines = rowsplitter.Split(str);
                var dict = new Dictionary<string,string>();
                foreach(var line in lines) 
                {
                    var match = keyvaluesplitter.Match(line);
                    if(match.Success)
                    {
                        var key = match.Groups[1].Value;
                        var value = match.Groups[2].Value;
                        dict.Add(key,value);
                    }
                }
                
                if(dict.ContainsKey("hue-bridgeid"))
                {
                    return ( data.RemoteEndPoint.Address, 0 , dict);
                }
            }
        }

        void Verbose(string s)
        {
            Console.WriteLine(s);
        }

        async Task<int> Run(string[] args)
        {
            Verbose("Finding hue bridge...");
            var (hueip,port,headers) = await FindHue();
            if(hueip==null) {
                Console.WriteLine("Hue bridge not found.");
                return -1;
            }
            
            Verbose($"Bridge found on {hueip}");

            return 0;
        }

        static async Task<int> Main(string[] args)
        {
            var p = new Program();
            return await p.Run(args);
            
        }
    }
}
