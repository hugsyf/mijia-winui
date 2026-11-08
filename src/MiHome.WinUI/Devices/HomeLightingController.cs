using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;

namespace MiHome.WinUI.Devices;

internal sealed record LightingResult(string Device, string Channel, string Outcome, string? Detail = null);

internal sealed class HomeLightingController(IDeviceService service)
{
    public static List<PropertySchema> PowerReadings(DeviceIdentity device, DeviceSchema schema) =>
        (device.Adapter is { } adapter
            ? adapter.Bindings.Where(binding => binding.Capability == "power").Select(binding => binding.Property)
            : DeviceExperience.PowerReadings(device, schema))
        .Where(property => property.Readable && property.Type == "bool")
        .DistinctBy(property => (property.Siid, property.Piid)).ToList();

    public async Task<List<LightingResult>> ExecuteAsync(List<DeviceTile> tiles, string? roomId, bool on,
        Action<DeviceCardState>? stateRead = null)
    {
        var results = new List<LightingResult>();
        var visited = new HashSet<(string Home, string Device, int Siid, int Piid)>();
        var visible = DeviceListing.Visible(tiles.Select(tile => tile.Device)).Select(device => device.Id).ToHashSet();
        foreach (var tile in tiles.DistinctBy(tile => tile.Device.Id).Where(tile => visible.Contains(tile.Device.Id) &&
            (roomId is null || tile.Device.RoomId == roomId) && DeviceCardPresentation.Category(tile.Device, tile.State) == "light"))
        {
            var device = tile.Device;
            if (!device.Online) { results.Add(new(device.Name, "", "skipped", "离线或状态未知")); continue; }
            DeviceSchema schema;
            try { schema = await service.GetSchemaAsync(device.Id); }
            catch (BridgeException error) when (error.Code == "auth") { throw; }
            catch { results.Add(new(device.Name, "", "skipped", "无法读取设备规格")); continue; }
            var session = new DeviceControlSession(device, schema, service);
            if (stateRead is not null) session.StateRead += stateRead;
            await session.RefreshAsync();
            if (session.Feedback is not null || session.State is null)
            { results.Add(new(device.Name, "", "skipped", "无法确认当前状态")); continue; }
            var powers = PowerReadings(device, schema);
            if (powers.Count == 0) { results.Add(new(device.Name, "", "skipped", "未提供电源控制")); continue; }
            foreach (var property in powers)
            {
                var physicalId = device.Id;
                foreach (var channel in DeviceModels.Find(device)?.Channels ?? [])
                {
                    var suffix = $".s{channel}";
                    if (physicalId.EndsWith(suffix, StringComparison.Ordinal)) { physicalId = physicalId[..^suffix.Length]; break; }
                }
                if (!visited.Add((device.HomeId, physicalId, property.Siid, property.Piid))) continue;
                var label = powers.Count > 1 ? DeviceModels.ChannelLabel(device, property.Siid) : "";
                if (DeviceExperience.IsWireless(device, session.State, property.Siid))
                { results.Add(new(device.Name, label, "skipped", "无线模式")); continue; }
                if (!DeviceExperience.CanWrite(device, session.State, property, on))
                { results.Add(new(device.Name, label, "skipped", property.Writable ? "当前状态不允许操作" : "只读属性")); continue; }
                // The shared session rechecks state immediately before each write and
                // confirms the result. It skips matching values and never retries a write.
                await session.WriteAsync(property, on);
                var confirmed = session.Feedback is null && DeviceCardPresentation.TryValue(session.State, property, out var value) && value.GetBoolean() == on;
                results.Add(new(device.Name, label, confirmed ? "confirmed" : "unconfirmed", session.Feedback));
            }
        }
        return results;
    }
}
