using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Devices;

internal static class AdapterConditions
{
    public static bool Matches(DeviceAdapter? adapter, DeviceCardState? state, AdapterCondition? condition)
    {
        if (condition is null) return true;
        var property=adapter?.Bindings.FirstOrDefault(binding=>binding.Id==condition.Binding)?.Property;
        return property is not null && DeviceCardPresentation.TryValue(state,property,out var value) && JsonElement.DeepEquals(value,condition.Value);
    }
    public static bool Enabled(DeviceAdapter? adapter,DeviceCardState? state,IEnumerable<AdapterCondition> conditions) =>
        conditions.All(condition=>Matches(adapter,state,condition));
}
