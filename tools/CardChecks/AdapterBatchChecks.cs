using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
internal static class AdapterBatchChecks
{
    public static void Run()
    {
        var checks=0;void Check(bool result,string name){if(!result)throw new Exception(name);checks++;}
        var device=new DeviceIdentity("parent","名称","xiaomi.switch.w2","urn:miot-spec-v2:device:switch:0000A023:xiaomi-w2:1:0000D031","home","room",true);
        var active=new DeviceAdapter("custom","专属","1.0","appliance",[],[],[],[],"");
        var candidates=AdapterBatchRules.ExportCandidates([device with{Id="parent.s2"},device,device with{Id="second"},device with{Id="custom",Adapter=active},device with{Id="new-spec",SpecType="another"}]);
        Check(candidates.Count==2&&candidates[0].Id=="parent","Deduplicate by model/spec and prefer complete parent schema");
        Check(AdapterBatchRules.ExportCandidates([device with{Adapter=active}]).Count==0,"Skip active custom adapters");
        var all=AdapterBatchRules.ExportCandidates([device with{Id="parent.s2"},device,device with{Id="custom",Adapter=active},device with{Id="new-model",Model="example.sensor.new",Adapter=active}],false);
        Check(all.Count==2&&!all.Single(d=>d.Model==device.Model).Id.Contains(".s",StringComparison.Ordinal)&&all.Any(d=>d.Model=="example.sensor.new"),"All-device export includes adapted models, deduplicates models/specs and prefers the complete parent");
        Check(AdapterBatchRules.ExportCandidates([device with{Adapter=active}],false).Single().Adapter==active,"All-device export retains active adapters");
        Check(AdapterBatchRules.ExportCandidates([],false).Count==0,"An empty home has no export candidates");
        using var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"device-card-specs.json")));
        var schema=fixtures.RootElement.GetProperty("xiaomi.switch.w2").Deserialize<DeviceSchema>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Check(!AdapterBatchRules.UsesDefaultPanel(device,schema),"An existing built-in panel is not an unadapted device");
        Check(AdapterBatchRules.UsesDefaultPanel(device,new([])),"A model with no usable schema still falls back to the default panel");
        Check(AdapterBatchRules.UsesDefaultPanel(device with{Model="example.sensor",SpecType="unknown"},schema),"Unknown model/spec retains default-panel eligibility");
        Check(!AdapterBatchRules.UsesDefaultPanel(device with{Adapter=active},schema),"An adapter overrides default classification");
        Check(AdapterBatchRules.OverlappingModels([new[]{"a","b"},new[]{"b"}]),"Refuse silently replacing model associations in the same import");
        Check(!AdapterBatchRules.OverlappingModels([new[]{"a"},new[]{"b"}]),"Disjoint model associations can be imported together");
        Check(AdapterBatchRules.DuplicateIds(["one","two","one"]).SetEquals(["one"]),"Duplicate package versions are excluded together");
        var packages=new[]{new AdapterPackage("b","B","1",["z"],false,false),new AdapterPackage("a","A","1",["a"],true,false)};
        Check(AdapterBatchRules.Sort(packages,"name",false).First().Id=="a"&&AdapterBatchRules.Sort(packages,"name",true).First().Id=="b","Name sorting respects direction");
        Check(AdapterBatchRules.Sort(packages,"model",false).First().Id=="a"&&AdapterBatchRules.Sort(packages,"state",false).First().Enabled,"Model/state sorting");
        Console.WriteLine($"Adapter batch: {checks} offline checks passed.");
    }
}
