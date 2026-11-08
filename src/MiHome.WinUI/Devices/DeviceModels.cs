using System.Text.Json;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

// Shared with the sidecar. Only add models after reviewing their public MIoT spec.
internal sealed record DeviceProfile(string Category, string[] Properties, int[]? Channels = null,
    bool WirelessMode = false, bool PurposeIcons = false);
internal static class DeviceModels
{
    internal static readonly Dictionary<string, DeviceProfile> Profiles = Load();
    private static Dictionary<string, DeviceProfile> Load()
    {
        using var stream = typeof(DeviceModels).Assembly.GetManifestResourceStream("MiHome.DeviceProfiles.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, DeviceProfile>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    internal static DeviceProfile? Find(DeviceIdentity device) => Profiles.GetValueOrDefault(device.Model);
    internal static bool IsSwitch(DeviceIdentity device) => Find(device)?.Category == "switch";
    internal static string ChannelLabel(DeviceIdentity device, int siid)
    {
        var channels = Find(device)?.Channels ?? [];
        var index = Array.IndexOf(channels, siid);
        if (channels.Length == 1) return "电源";
        if (channels.Length == 2 && index >= 0) return index == 0 ? "左键" : "右键";
        if (channels.Length == 3 && index >= 0) return new[] { "左键", "中键", "右键" }[index];
        return $"通道 {siid}";
    }
}
