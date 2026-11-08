using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

internal static class DeviceOrdering
{
    public static List<DeviceIdentity> Apply(IEnumerable<DeviceIdentity> devices, IReadOnlyList<string> order)
    {
        var ranks = order.Select((id, index) => (id, index)).DistinctBy(x => x.id).ToDictionary(x => x.id, x => x.index);
        return devices.OrderBy(d => ranks.GetValueOrDefault(d.Id, int.MaxValue))
            .ThenBy(d => ranks.ContainsKey(d.Id) ? 0 : DeviceExperience.DefaultSize(d) == "compact" ? 1 : 0).ToList();
    }
    public static List<string> Move(IReadOnlyList<string> order, string moved, string target, bool after)
    {
        var result = order.Distinct().ToList();
        if (moved == target || !result.Contains(moved) || !result.Contains(target)) return result;
        result.Remove(moved);
        result.Insert(result.IndexOf(target) + (after ? 1 : 0), moved);
        return result;
    }
}
