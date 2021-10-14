using System.Collections.Generic;
using System.Net;

public struct HueFoundResponse
{
    public bool Found;
    public IPAddress Ip;
    public int? Port;
    public Dictionary<string, string> Headers;
}