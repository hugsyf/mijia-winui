using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Devices;

internal sealed record ExpandedCardItem(AdapterWidget Widget, PropertySchema? Property, AdapterAction? Action = null, List<PropertySchema>? Alternatives = null);
internal static class ExpandedCardPlan
{
    public static List<ExpandedCardItem> Build(DeviceIdentity device, DeviceSchema schema)
    {
        if(device.Adapter is {CardExpandedDeclared:true,CardExpanded.Count:0})return [];
        if (device.Adapter is { CardExpanded.Count: > 0 } adapter)
        {
            var declared=adapter.CardExpanded.Select(w=>AdapterLocalizationResolver.Widget(adapter,w)).Select(w=> w.Kind=="action"
                ? new ExpandedCardItem(w,null,adapter.Actions.Single(a=>a.Id==w.Action))
                : new ExpandedCardItem(w,AdapterControlValues.WithLabels(adapter.Bindings.Single(b=>b.Id==w.Binding).Property,w.Options))).ToList();
            if(declared.Count==2 && declared.All(i=>i.Widget.When is null && i.Property is not null) && declared.Select(i=>i.Widget.Kind).ToHashSet().SetEquals(["brightness","colorTemperature"]) && declared[0].Property!.Siid==declared[1].Property!.Siid)
                return [declared[0] with {Alternatives=declared.Select(i=>i.Property!).ToList()}];
            return declared;
        }
        // Existing packages retain their declared permissions when using built-in quick controls.
        var properties = device.Adapter?.Bindings.Select(b=>b.Property).ToList() ?? schema.Properties;
        PropertySchema? Find(string service,string name,bool write=false)
        {
            var found=properties.Where(p=>p.Readable && (!write || p.Writable && (device.Adapter is null || device.Adapter.Bindings.Any(b=>b.AllowWrite && b.Property.Siid==p.Siid && b.Property.Piid==p.Piid))) && DeviceCardPresentation.Semantic(p.ServiceType)==service && DeviceCardPresentation.Semantic(p.PropertyType)==name &&
                (DeviceModels.Find(device) is not null || p.ServiceType.StartsWith("urn:miot-spec-v2:") && p.PropertyType.StartsWith("urn:miot-spec-v2:"))).ToList();
            return found.Count==1 ? found[0] : null;
        }
        ExpandedCardItem Item(string kind,string label,PropertySchema p)=>new(new(kind,label,null,null,null),p);
        var brightness=Find("light","brightness",true);
        var temperature=Find("light","color-temperature",true);
        if (brightness?.Unit=="percentage" || temperature?.Unit=="kelvin")
        {
            var alternatives=new List<PropertySchema>();
            if(brightness?.Unit=="percentage" && Numeric(brightness)) alternatives.Add(brightness);
            if(temperature?.Unit=="kelvin" && Numeric(temperature)) alternatives.Add(temperature);
            if(alternatives.Count>0) return [new(new(alternatives[0]==brightness?"brightness":"colorTemperature",alternatives[0]==brightness?"亮度":"色温",null,null,null),alternatives[0],Alternatives:alternatives)];
        }
        var result=new List<ExpandedCardItem>();
        var target=Find("humidifier","target-humidity",true);
        if(target?.Unit=="percentage" && Numeric(target))
        {
            result.Add(Item("slider","目标湿度",target));
            if(Find("humidifier","mode",true) is {} mode && mode.Values.Count>0) result.Add(Item("choice","运行模式",mode));
            return result;
        }
        if(Find("speaker","volume",true) is {} volume && Numeric(volume)) return [Item("slider","音量",volume)];
        foreach(var service in new[]{"fan","air-purifier"})
        {
            if(Find(service,"fan-level",true) is {} level)
            {
                if(level.Values.Count>0) result.Add(Item("choice","风速",level));
                else if(Numeric(level)) result.Add(Item("slider","风速",level));
            }
            if(Find(service,"horizontal-swing",true) is {Type:"bool"} swing) result.Add(Item("toggle","摇头",swing));
            if(result.Count==0 && Find(service,"mode",true) is {} mode && mode.Values.Count>0) result.Add(Item("choice","模式",mode));
            if(result.Count>0) return result.Take(2).ToList();
        }
        if(Find("curtain","target-position",true) is {Unit:"percentage"} position && Numeric(position))
        {
            result.Add(Item("slider","目标开合度",position));
            if(Find("curtain","motor-control",true) is {} motor && motor.Values.Count>0) result.Add(Item("choice","电机控制",motor));
            return result;
        }
        if(device.Model=="cuco.plug.v3" && Find("quick-countdown","left-time",true) is {} delay && Numeric(delay) && delay.Unit is "seconds" or "minutes" or "hours" && Find("quick-countdown","on",true) is {Type:"bool"} enabled)
            return [Item("duration","倒计时时长",delay),Item("toggle","启用倒计时",enabled)];
        foreach(var key in new[]{("router","download-speed","下载"),("router","upload-speed","上传"),("temperature-humidity-sensor","temperature","温度"),("temperature-humidity-sensor","relative-humidity","湿度"),("air-fryer","status","烹饪状态"),("air-fryer","left-time","剩余时间"),("power-consumption","electric-power","当前功率"),("occupancy-sensor","occupancy-status","感应状态")})
            if(Find(key.Item1,key.Item2) is {} property) result.Add(Item(key.Item2=="left-time" && property.Unit is "minutes" or "seconds" ? "countdown":"metric",key.Item3,property));
        return result.Take(2).ToList();
    }
    private static bool Numeric(PropertySchema p)=>p.Type is not("bool" or "string") && p.Values.Count==0 && p.Range.Length>=2 && p.Range.All(double.IsFinite) && p.Range[1]>p.Range[0];
}
