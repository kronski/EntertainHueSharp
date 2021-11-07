using CommandLine;

public class Options
{

    [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
    public bool Verbose { get; set; }
    [Option('g', "groupid", Required = false, HelpText = "Specify entertainment groupid.")]
    public string GroupId { get; set; }
    [Option('i', "ip", Required = false, HelpText = "Hue ip.")]
    public string IpNumber { get; set; }

    [Option('x', Required = false, HelpText = "Crop x")]
    public double? X { get; set; }
    [Option('y', Required = false, HelpText = "Crop y")]
    public double? Y { get; set; }
    [Option('w', Required = false, HelpText = "Crop width")]
    public double? Width { get; set; }
    [Option('h', Required = false, HelpText = "Crop width")]
    public double? Height { get; set; }
    [Option("picture", Required = false, HelpText = "Take picture")]
    public bool Picture { get; set; }

    [Option("cameraframes", Required = false, HelpText = "Diagnose camera fps")]
    public bool CameraFrames { get; set; }

    [Option("calibrate", Required = false, HelpText = "Calibrate lamps with web page")]
    public bool Calibrate { get; set; }

    [Option("randomlight", Required = false, HelpText = "Random colors sent to Light")]
    public bool RandomLight { get; set; }

    static Options current;
    public static Options Current => current;
    public static Options Parse(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args).WithParsed(x => { current = x; });
        return current;
    }
}