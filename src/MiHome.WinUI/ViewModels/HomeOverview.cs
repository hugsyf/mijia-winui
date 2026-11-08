using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
namespace MiHome.WinUI.ViewModels;

internal sealed record HomeEnvironment(string RoomId,string Room,DeviceTile? Source,List<DeviceTile> Candidates,double? Temperature,double? Humidity);
internal sealed record HomeLighting(string RoomId,string Room,int On,int Off,int Unknown);
internal sealed record HomeAttention(DeviceTile Tile,string Message);
internal sealed record HomeNetwork(DeviceTile Tile,string Summary);
internal sealed record HomeOverview(List<HomeEnvironment> Environment,List<HomeLighting> Lighting,List<DeviceTile> Sensors,List<HomeAttention> Attention,List<DeviceTile> Pins,List<HomeNetwork> Network)
{
    public static HomeOverview Build(List<DeviceTile> tiles,HomePreferences preferences)
    {
        double? Number(DeviceTile t,string capability,string service,string property)=>AdapterReadings.Capability(t.Device,t.State,capability)??DeviceCardPresentation.Number(t.State,service,property);
        var environment=new List<HomeEnvironment>();var lighting=new List<HomeLighting>();var sensors=new List<DeviceTile>();var attention=new List<HomeAttention>();var network=new List<HomeNetwork>();
        foreach(var room in tiles.GroupBy(t=>t.Device.RoomId))
        {
            var candidates=room.Where(t=>DeviceCardPresentation.Category(t.Device,t.State)=="temperature-humidity-sensor").ToList();
            if(candidates.Count>0)
            {
                var source=candidates.FirstOrDefault(t=>preferences.Sources.GetValueOrDefault(room.Key)==t.Device.Id)??(candidates.Count==1?candidates[0]:null);
                environment.Add(new(room.Key,room.First().Room,source,candidates,
                    source is {Device.Online:true}?Number(source,"temperature","temperature-humidity-sensor","temperature"):null,
                    source is {Device.Online:true}?Number(source,"humidity","temperature-humidity-sensor","relative-humidity"):null));
            }
            var lights=room.Where(t=>DeviceCardPresentation.Category(t.Device,t.State)=="light").ToList();
            if(lights.Count>0)
            {
                var on=0;var off=0;var unknown=0;
                foreach(var light in lights)
                {
                    var props=HomeLightingController.PowerReadings(light.Device,light.State?.Schema??new([]));
                    if(props.Count==0){unknown++;continue;}
                    foreach(var p in props)
                    {
                        if(DeviceExperience.IsWireless(light.Device,light.State,p.Siid))continue;
                        if(!light.Device.Online||!DeviceCardPresentation.TryValue(light.State,p,out var value)||value.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))unknown++;
                        else if(value.GetBoolean())on++;else off++;
                    }
                }
                if(on+off+unknown>0)lighting.Add(new(room.Key,room.First().Room,on,off,unknown));
            }
        }
        foreach(var tile in tiles)
        {
            var category=DeviceCardPresentation.Category(tile.Device,tile.State);
            if(category is "magnet-sensor" or "occupancy-sensor")sensors.Add(tile);
            if(!tile.Device.Online||tile.State is null)continue;
            var fault=tile.State.Schema.Properties.Where(p=>DeviceCardPresentation.Semantic(p.PropertyType)=="fault")
                .Any(p=>DeviceCardPresentation.TryValue(tile.State,p,out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.Number&&v.TryGetDouble(out var n)&&n!=0);
            var battery=Number(tile,"battery","battery","battery-level")??Number(tile,"battery","single-use-battery","battery-level");
            var filter=Number(tile,"filter","filter","filter-life-level");
            if(fault)attention.Add(new(tile,tile.Summary=="缺水"?"缺水":"设备异常"));
            else if(battery is >=0 and <=15)attention.Add(new(tile,Infrastructure.UiText.Format("BatteryValue",battery.Value.ToString("0"))));
            else if(filter is >=0 and <=20)attention.Add(new(tile,Infrastructure.UiText.Format("FilterValue",filter.Value.ToString("0"))));
            if(category=="router")
            {
                var count=Number(tile,"connected","router","connected-device-number");
                var down=DeviceCardPresentation.RouterSpeed(tile.State,"download-speed");
                var up=DeviceCardPresentation.RouterSpeed(tile.State,"upload-speed");
                var summary=string.Join(" · ",new[]{count is {} n?$"{n:0} 台设备已连接":"",down is not null?"↓ "+down:"",up is not null?"↑ "+up:""}.Where(s=>s.Length>0));
                if(summary.Length>0)network.Add(new(tile,summary));
            }
        }
        var byId=tiles.ToDictionary(t=>t.Device.Id);
        return new(environment,lighting,sensors,attention,preferences.Pins.Where(byId.ContainsKey).Select(id=>byId[id]).ToList(),network);
    }
}
