using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
internal static class ExpandedChecks
{
    public static void Run()
    {
        var checks=0;void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
        var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"device-card-specs.json"))).RootElement;
        var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        (DeviceIdentity Device,DeviceSchema Schema) Fixture(string model)
        {var item=fixtures.GetProperty(model);return(new("device","设备",model,item.GetProperty("specType").GetString()!,"home","房间",true),item.Deserialize<DeviceSchema>(json)!);}
        var light=Fixture("yeelink.light.bslamp2");var items=ExpandedCardPlan.Build(light.Device,light.Schema);
        Check(items.Count==2&&items[0].Alternatives?.Count==2&&items[1].Widget.Kind=="colorPresets","Brightness and temperature share one slider beside color presets");
        var humidifier=Fixture("xiaomi.humidifier.600");items=ExpandedCardPlan.Build(humidifier.Device,humidifier.Schema);
        Check(items.Count==2&&items.Any(i=>i.Widget.Label=="目标湿度")&&items.Any(i=>i.Widget.Kind=="choice"),"Humidifier exposes target and actual mode enum");
        var speaker=Fixture("xiaomi.wifispeaker.l15a");Check(ExpandedCardPlan.Build(speaker.Device,speaker.Schema).Single().Widget.Label=="音量","Speaker volume shortcut");
        var plug=Fixture("cuco.plug.v3");items=ExpandedCardPlan.Build(plug.Device,plug.Schema);
        Check(items.Select(i=>i.Widget.Kind).SequenceEqual(new[]{"duration","toggle"}),"Countdown duration does not secretly enable timer");
        var relay=Fixture("xiaomi.switch.w2");Check(!ExpandedCardPlan.Build(relay.Device,relay.Schema).Any(i=>i.Widget.Kind=="brightness"),"A relay labeled as a lamp is not a dimmer");
        var group=ExpandedCardPlan.Build(relay.Device,relay.Schema).Single();
        Check(group.Widget is{Kind:"switchGroup",Children.Count:2}&&group.Widget.Children.Select(child=>CardCompositePlan.Property(relay.Device,relay.Schema,child)).Select(p=>(p.Siid,p.Piid)).Distinct().Count()==2,"A multi-channel relay exposes independent verified power properties");
        var oldAdapter=new DeviceAdapter("example.readonly","只读","1.0.0","light",light.Schema.Properties.Select((p,i)=>new AdapterBinding("p"+i,p,false,"")).ToList(),[],[],[],"");
        Check(!ExpandedCardPlan.Build(light.Device with{Adapter=oldAdapter},light.Schema).Any(i=>i.Property?.Writable==true),"Auto controls do not invent adapter write permission");
        var bright=light.Schema.Properties.Single(p=>DeviceCardPresentation.Semantic(p.PropertyType)=="brightness");
        var temp=light.Schema.Properties.Single(p=>DeviceCardPresentation.Semantic(p.PropertyType)=="color-temperature");
        var declared=oldAdapter with{Bindings=[new("bright",bright,true,""),new("temp",temp,true,"")],CardExpanded=[new("brightness","亮度","bright",null,null),new("colorTemperature","色温","temp",null,null)]};
        Check(ExpandedCardPlan.Build(light.Device with{Adapter=declared},light.Schema).Single().Alternatives?.Count==2,"SDK lamp shortcuts share compact presentation");
        var schema=new DeviceSchema([]);var state=new DeviceCardState("device",schema,JsonSerializer.SerializeToElement(Array.Empty<object>()),DateTimeOffset.UtcNow,null);
        var live=new LiveCardSnapshot(state);var notifications=0;live.Changed+=()=>notifications++;
        live.Set(state);Check(notifications==0,"Same snapshot does not recreate controls");
        live.Set(state with{ReadAt=DateTimeOffset.UtcNow.AddSeconds(1)});Check(notifications==1,"New snapshot has one in-place notification");
        var action=new ActionSchema(2,1,"暂停","urn:miot-spec-v2:service:demo:1","urn:miot-spec-v2:action:pause:1",[],[]);
        var adapter=oldAdapter with{Actions=[new("pause",action)],CardExpanded=[new("action","暂停",null,null,null){Action="pause"}]};
        Check(ExpandedCardPlan.Build(light.Device with{Adapter=adapter},light.Schema).Single().Action?.Id=="pause","Action slot resolves declared action, not a property write");
        Check(ExpandedCardPlan.Build(light.Device with{Adapter=oldAdapter with{CardExpandedDeclared=true,CardExpanded=[]}},light.Schema).Count==0,"An explicit empty card layout disables automatic shortcuts");
        var mediaAction=action with{ServiceType="urn:miot-spec-v2:service:play-control:1"};
        Check(CardCompositePlan.IsMediaAction(mediaAction)&&!CardCompositePlan.IsMediaAction(mediaAction with{Inputs=[bright]})&&!CardCompositePlan.IsMediaAction(mediaAction with{ActionType="urn:miot-spec-v2:action:resume-cook:1"}),"Media shortcuts allow only standard input-free transport actions");
        var mediaSchema=speaker.Schema with{Actions=[mediaAction]};
        var mediaPlan=ExpandedCardPlan.Build(speaker.Device,mediaSchema).Single();
        Check(mediaPlan.Widget.Kind=="media"&&mediaPlan.Widget.Children!.Any(child=>child.Kind=="slider")&&CardCompositePlan.Action(speaker.Device,mediaSchema,mediaPlan.Widget.Children!.Single(child=>child.Kind=="action")).Action==mediaAction,"Native speaker transport uses the actual MIOT action and volume schema");
        var playingProperty=mediaSchema.Properties.Single(p=>DeviceCardPresentation.Semantic(p.PropertyType)=="playing-state");
        DeviceCardState Playback(object value)=>new("device",mediaSchema,JsonSerializer.SerializeToElement(new[]{new{siid=playingProperty.Siid,piid=playingProperty.Piid,code=0,value}}),DateTimeOffset.UtcNow,null);
        Check(CardCompositePlan.Playing(Playback(1),playingProperty) is true&&CardCompositePlan.Playing(Playback(2),playingProperty) is false,"Standard playback state chooses the appropriate play/pause button");
        Check(CardCompositePlan.Playing(Playback("Stop"),playingProperty with{Type="string",Values=[]}) is null&&CardCompositePlan.Playing(null,playingProperty) is null,"Missing or textual playback states do not select a guessed transport state");
        var composite=adapter with{CardExpanded=[new("media","播放",null,[new("action","暂停",null,null,null){Action="pause"}],null)]};
        Check(ExpandedCardPlan.Build(light.Device with{Adapter=composite},light.Schema).Single() is{Property:null,Widget.Kind:"media"},"Declared composites remain one layout item and retain their child declarations");
        PropertySchema P(string service,string name,int id,string type,string unit,bool writable,double[] range,List<EnumValue>? values=null)=>new(2,id,service,name,type,unit,true,writable,range,values??[]){ServiceType=$"urn:miot-spec-v2:service:{service}:1",PropertyType=$"urn:miot-spec-v2:property:{name}:1"};
        var fan=new DeviceIdentity("fan","风扇","example.fan.one","urn:miot-spec-v2:device:fan:1","home","房间",true);
        var fanSchema=new DeviceSchema([P("fan","on",1,"bool","",true,[]),P("fan","fan-level",2,"uint8","",true,[],[new(JsonSerializer.SerializeToElement(1),"低"),new(JsonSerializer.SerializeToElement(2),"高")]),P("fan","horizontal-swing",3,"bool","",true,[])]);
        Check(ExpandedCardPlan.Build(fan,fanSchema).Select(i=>i.Widget.Kind).SequenceEqual(new[]{"choice","toggle"}),"Fan shortcuts use actual levels and swing capability");
        var curtain=fan with{Model="example.curtain.one",SpecType="urn:miot-spec-v2:device:curtain:1"};
        var motor=P("curtain","motor-control",3,"uint8","",true,[],[new(JsonSerializer.SerializeToElement(0),"停止"),new(JsonSerializer.SerializeToElement(1),"打开")]);
        var curtainSchema=new DeviceSchema([P("curtain","current-position",1,"uint8","percentage",false,[0,100,1]),P("curtain","target-position",2,"uint8","percentage",true,[0,100,1]),motor]);
        var curtainState=new DeviceCardState(curtain.Id,curtainSchema,JsonSerializer.SerializeToElement(new[]{new{siid=2,piid=1,code=0,value=50},new{siid=2,piid=2,code=0,value=50},new{siid=2,piid=3,code=0,value=0}}),DateTimeOffset.UtcNow,null);
        Check(ExpandedCardPlan.Build(curtain,curtainSchema).Count==2&&DeviceExperience.CanWrite(curtain,curtainState,motor,null),"Curtain enum control does not require an invented numeric range");
        Console.WriteLine($"Expanded cards: {checks} offline checks passed.");
    }
}
