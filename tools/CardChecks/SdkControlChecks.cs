using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
internal static class SdkControlChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
        var p = new PropertySchema(2, 2, "light", "亮度", "uint8", "percentage", true, true, [1, 100, 1], []);
        Check(AdapterControlValues.IsValid(p, 1) && AdapterControlValues.IsValid(p, 100), "Actual endpoints remain valid");
        Check(!AdapterControlValues.IsValid(p, 0) && !AdapterControlValues.IsValid(p, 101), "Do not invent a zero brightness value");
        Check(!AdapterControlValues.IsValid(p, 50.5), "Integer properties reject fractions");
        Check(!AdapterControlValues.IsValid(p, double.NaN) && !AdapterControlValues.IsValid(p, double.PositiveInfinity), "Reject non-finite input");
        Check(AdapterControlValues.IsValid(p with { Range = [1700, 6500, 100], Type = "uint32" }, 1800), "Steps use actual minimum as origin");
        Check(!AdapterControlValues.IsValid(p with { Range = [1700, 6500, 100], Type = "uint32" }, 1750), "Reject off-step input");
        Check(AdapterControlValues.Rgb(255, 128, 1) == 0xff8001, "RGB channel packing");
        Check(!AdapterControlValues.IsValid(p with { Range = [1, 16777215, 1], Type = "uint32" }, AdapterControlValues.Rgb(0, 0, 0)), "Device color range can exclude black");
        Check(AdapterControlValues.Duration(90, "minutes") == "01:30:00", "Minutes are converted once");
        Check(AdapterControlValues.Duration(90, "seconds") == "00:01:30", "Seconds retain their unit");
        Check(AdapterControlValues.Duration(24, "hours") == "24:00:00", "Duration does not wrap at midnight");
        Check(AdapterControlValues.Duration(double.NaN, "seconds") == "—" && AdapterControlValues.Duration(1, "unknown") == "—", "Unknown is not zero");
        var widget = JsonSerializer.Deserialize<AdapterWidget>("{\"kind\":\"countdown\",\"label\":\"剩余\",\"binding\":\"time\",\"activeBinding\":\"enabled\"}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Check(widget.ActiveBinding == "enabled" && widget.Presentation == "auto", "New fields and defaults deserialize");
        Check(AdapterControlValues.Levels(p with { Range = [2, 8, 2] }).SequenceEqual(new double[] { 2, 4, 6, 8 }), "Levels follow actual origin and step");
        Check(AdapterControlValues.Levels(p with { Range = [1, 100, 1] }).Length == 0, "Large ranges do not become 100 buttons");
        Check(AdapterControlValues.Levels(p with { Range = [1, 6, 2] }).Length == 0, "Do not invent a last partial level");
        Check(AdapterControlValues.Levels(p with { Range = [0, 1, 0] }).Length == 0, "Invalid steps are rejected");
        Check(AdapterControlValues.Gauge(p with { Type = "float", Range = [-20, 80, 1] }, 30) == 50, "Gauge normalizes actual negative and positive limits");
        Check(AdapterControlValues.Gauge(p, null) is null && AdapterControlValues.Gauge(p, 101) is null, "Unknown and out-of-range gauges do not invent zero readings");
        Console.WriteLine($"SDK controls: {checks} checks passed (offline, no device writes).");
    }
}
