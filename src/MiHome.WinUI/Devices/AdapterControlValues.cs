using MiHome.WinUI.Core;
using System.Text.Json;
namespace MiHome.WinUI.Devices;

internal static class AdapterControlValues
{
    public static PropertySchema WithLabels(PropertySchema property, List<AdapterOption>? options) => options is null ? property : property with
    {
        Values = property.Values.Select(v => options.FirstOrDefault(o => JsonElement.DeepEquals(v.Value, o.Value)) is {} option
            ? v with { DisplayDescription = option.Label } : v).ToList()
    };
    public static double[] Levels(PropertySchema property)
    {
        if (!(property.Type.StartsWith("int") || property.Type.StartsWith("uint")) || property.Range.Length < 3 || !property.Range.All(double.IsFinite) || property.Range.Any(n => n != Math.Truncate(n)) || property.Range[2] <= 0) return [];
        var count = (property.Range[1] - property.Range[0]) / property.Range[2];
        if (count < 1 || count > 7 || Math.Abs(count - Math.Round(count)) > 0.000001) return [];
        return Enumerable.Range(0, (int)Math.Round(count) + 1).Select(i => property.Range[0] + i * property.Range[2]).ToArray();
    }
    public static double? Gauge(PropertySchema property, double? value)
    {
        if (value is not {} n || !IsValid(property, n) || property.Range[1] <= property.Range[0]) return null;
        return 100 * (n - property.Range[0]) / (property.Range[1] - property.Range[0]);
    }
    public static bool IsValid(PropertySchema property, double value)
    {
        if (!double.IsFinite(value) || property.Range.Length < 2 || value < property.Range[0] || value > property.Range[1]) return false;
        if (property.Type.StartsWith("int") || property.Type.StartsWith("uint"))
            if (value != Math.Truncate(value)) return false;
        if (property.Range.Length > 2 && property.Range[2] > 0)
        {
            var steps = (value - property.Range[0]) / property.Range[2];
            if (Math.Abs(steps - Math.Round(steps)) > 0.00001) return false;
        }
        return true;
    }
    public static double SecondsPerUnit(string unit) => unit switch { "seconds" => 1, "minutes" => 60, "hours" => 3600, _ => double.NaN };
    public static string Duration(double value, string unit)
    {
        var seconds = value * SecondsPerUnit(unit);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) return "—";
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
    public static int Rgb(byte red, byte green, byte blue) => (red << 16) | (green << 8) | blue;
}
