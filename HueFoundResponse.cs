using System.Collections.Generic;
using System.Net;

public struct HueFindResponse
{
    public bool Found;
    public IPAddress Ip;
    public int? Port;
    public Dictionary<string, string> Headers;
}