using System.Globalization;
using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Devices;

internal static class AdapterReadings
{
    public static string Format(DeviceCardState? state,PropertySchema property)
    {
        if(!DeviceCardPresentation.TryValue(state,property,out var value))return "—";
        var choice=property.Values.FirstOrDefault(v=>JsonElement.DeepEquals(v.Value,value));
        if(choice is not null)return Panels.PropertyPresentation.EnumLabel(property,choice);
        if(value.ValueKind is JsonValueKind.True or JsonValueKind.False)return Infrastructure.UiText.Get(value.GetBoolean()?"TurnedOn":"TurnedOff");
        if(value.ValueKind==JsonValueKind.String)return value.GetString()??"—";
        var unit=Panels.PropertyPresentation.Unit(property.Unit);
        return value.TryGetDouble(out var number)?number.ToString("0.#",CultureInfo.CurrentCulture)+(unit.Length>0?" "+unit:""):"—";
    }
    public static double? Capability(DeviceIdentity device,DeviceCardState? state,string capability)
    {
        var binding=device.Adapter?.Bindings.FirstOrDefault(b=>b.Capability==capability);
        return binding is not null && DeviceCardPresentation.TryValue(state,binding.Property,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number)?number:null;
    }
}
