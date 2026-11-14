using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
using System.Text.Json;
namespace MiHome.WinUI.Devices;

internal static class CardCompositePlan
{
    public static bool IsComposite(string kind) => kind is "colorPresets" or "thermostat" or "media" or "switchGroup";
    public static string BindingId(PropertySchema property) => $"native{property.Siid}_{property.Piid}";
    public static string ActionId(ActionSchema action) => $"native{action.Siid}_{action.Aiid}";
    public static bool IsMediaAction(ActionSchema action) => action.Inputs.Count==0 &&
        action.ServiceType.StartsWith("urn:miot-spec-v2:service:play-control:",StringComparison.Ordinal) &&
        action.ActionType.StartsWith("urn:miot-spec-v2:action:",StringComparison.Ordinal) &&
        DeviceCardPresentation.Semantic(action.ActionType) is "previous" or "play" or "pause" or "next";
    public static PropertySchema Property(DeviceIdentity device,DeviceSchema schema,AdapterWidget widget) =>
        device.Adapter is {} adapter ? AdapterControlValues.WithLabels(adapter.Bindings.Single(b=>b.Id==widget.Binding).Property,widget.Options) :
        schema.Properties.Single(p=>BindingId(p)==widget.Binding);
    public static AdapterAction Action(DeviceIdentity device,DeviceSchema schema,AdapterWidget widget) =>
        device.Adapter is {} adapter ? adapter.Actions.Single(a=>a.Id==widget.Action) :
        new(widget.Action!,schema.Actions.Single(a=>ActionId(a)==widget.Action));
    public static AdapterWidget Child(string kind,string label,PropertySchema property) => new(kind,label,BindingId(property),null,null);
    public static bool? Playing(DeviceCardState? state,PropertySchema property) =>
        property.PropertyType.StartsWith("urn:miot-spec-v2:property:playing-state:",StringComparison.Ordinal)&&
        DeviceCardPresentation.TryValue(state,property,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var code)&&code is >=0 and <=2
            ?code==1:null;
}
