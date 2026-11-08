using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

internal static class DeviceListing
{
    // Hide a physical device only when all verified channels have cloud records.
    public static List<DeviceIdentity> Visible(IEnumerable<DeviceIdentity> devices)
    {
        var all = devices.ToList();
        var identities = all.Select(d => (d.HomeId, d.Model, d.Id)).ToHashSet();
        return all.Where(d => DeviceModels.Find(d)?.Channels is not { Length: > 0 } channels ||
            !channels.All(siid => identities.Contains((d.HomeId, d.Model, d.Id + $".s{siid}")))).ToList();
    }
    public static DeviceIdentity? Parent(DeviceIdentity device, IEnumerable<DeviceIdentity> devices)
    {
        var channels = DeviceModels.Find(device)?.Channels ?? [];
        foreach (var siid in channels)
        {
            var suffix = $".s{siid}";
            if (device.Id.EndsWith(suffix, StringComparison.Ordinal))
                return devices.FirstOrDefault(d => d.Id == device.Id[..^suffix.Length] && d.HomeId == device.HomeId && d.Model == device.Model);
        }
        return null;
    }
}
