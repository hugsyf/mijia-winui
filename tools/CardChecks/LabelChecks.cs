using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Panels;
internal static class LabelChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
        var value = JsonSerializer.SerializeToElement(20);
        var p = new PropertySchema(2,3,"Humidifier","Mode","uint8","",true,true,[],[new(value,"Quiet") { DisplayDescription="静音" }])
        { ServiceType="urn:miot-spec-v2:service:humidifier:0000780E", PropertyType="urn:miot-spec-v2:property:mode:00000008" };
        Check(PropertyPresentation.Name(p) == "模式", "Reviewed standard terminology is a fallback");
        Check(PropertyPresentation.Name(p with { DisplayName="运行方式" }) == "运行方式", "Official display name takes priority");
        Check(p.Name=="Mode" && p.Values[0].Description=="Quiet", "Raw labels are retained");
        Check(PropertyPresentation.Value(p,value)=="静音", "Enum reads use localized labels");
        var alias = AdapterControlValues.WithLabels(p,[new(value,"安静")]);
        Check(alias.Values[0].DisplayLabel=="安静", "Adapter option label takes priority");
        Check(alias.Values[0].Value.GetInt32()==20 && alias.Values[0].Description=="Quiet", "Alias does not alter payload or raw description");
        Check(p.Values[0].DisplayLabel=="静音", "Applying aliases does not mutate original schema");
        var custom=p with {PropertyType="urn:vendor-spec:property:mode:00000008"};
        Check(PropertyPresentation.Name(custom)=="Mode" && !PropertyPresentation.IsPrimary(custom), "Do not guess custom internal semantics");
        Check(PropertyPresentation.Service(p with {DisplayService="加湿器"})=="加湿器", "Service names use official display labels");
        Check(new EnumValue(value,"Quiet").DisplayLabel=="Quiet", "Missing translations keep original labels");
        Check(PropertyPresentation.Unit("celsius")=="°C", "Unit display does not affect MIOT unit identity");
        Console.WriteLine($"Labels: {checks} offline checks passed.");
    }
}
