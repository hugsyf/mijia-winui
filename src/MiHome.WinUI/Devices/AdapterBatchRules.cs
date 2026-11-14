using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

internal static class AdapterBatchRules
{
    public static IEnumerable<AdapterPackage> Sort(IEnumerable<AdapterPackage> packages,string key,bool descending)
    {
        string Value(AdapterPackage p)=>key switch {"model"=>p.Models.FirstOrDefault()??"","state"=>p.Enabled?"0":"1",_=>p.Name};
        return (descending?packages.OrderByDescending(Value,StringComparer.CurrentCultureIgnoreCase):packages.OrderBy(Value,StringComparer.CurrentCultureIgnoreCase))
            .ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(p=>p.Id,StringComparer.Ordinal);
    }
    public static List<DeviceIdentity> ExportCandidates(IEnumerable<DeviceIdentity> devices,bool onlyUnadapted=true)=>devices
        .Where(d=>!onlyUnadapted||d.Adapter is null).GroupBy(d=>(d.Model,d.SpecType))
        .Select(g=>g.OrderBy(d=>d.Id.Contains(".s",StringComparison.Ordinal)).ThenBy(d=>d.Id,StringComparer.Ordinal).First()).ToList();
    public static bool UsesDefaultPanel(DeviceIdentity device,DeviceSchema schema)=>device.Adapter is null&&
        (schema.Properties.Count==0||DeviceExperience.Kind(device,schema)==ExperienceKind.Generic);
    public static bool OverlappingModels(IEnumerable<IEnumerable<string>> models)=>models.SelectMany(m=>m).GroupBy(m=>m,StringComparer.Ordinal).Any(g=>g.Count()>1);
    public static HashSet<string> DuplicateIds(IEnumerable<string> ids)=>ids.GroupBy(id=>id,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
}
