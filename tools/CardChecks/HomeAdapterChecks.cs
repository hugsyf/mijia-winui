using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;

internal static class HomeAdapterChecks
{
    public static void Run()
    {
        var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"device-card-specs.json"))).RootElement;
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        DeviceIdentity Device(string model,string id,string room)=>new(id,id,model,json.GetProperty(model).GetProperty("specType").GetString()!,"home",room,true){RoomId=room};
        DeviceCardState State(string model,params (int S,int P,object V)[] values)=>new("",json.GetProperty(model).Deserialize<DeviceSchema>(options)!,JsonSerializer.SerializeToElement(values.Select(v=>new{siid=v.S,piid=v.P,code=0,value=v.V})),DateTimeOffset.UtcNow,null);
        var checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        var thermometer=Device("miaomiaoce.sensor_ht.t9","temp1","living");
        var tempState=State(thermometer.Model,(3,1001,0d),(3,1002,0),(2,1003,10));
        var tiles=new List<DeviceTile>{new(thermometer,tempState),new(thermometer with{Id="temp2"},tempState)};
        var overview=HomeOverview.Build(tiles,new([],[]));
        Check(overview.Environment.Single().Source is null,"Multiple sensors require an explicit source");
        Check(overview.Pins.Count==0,"Home does not pin arbitrary devices");
        overview=HomeOverview.Build(tiles,new(["temp2"],new(){["living"]="temp1"}));
        Check(overview.Environment.Single().Temperature==0 && overview.Environment.Single().Humidity==0,"Zero readings remain valid");
        Check(overview.Pins.Single().Device.Id=="temp2","Pinned device selection is honored");
        Check(overview.Attention.Count==2,"Low batteries appear once per device");
        overview=HomeOverview.Build([new(thermometer with{Online=false},tempState)],new([],[]));
        Check(overview.Environment.Single().Temperature is null && overview.Attention.Count==0,"Offline cached values do not imply current readings or alerts");
        overview=HomeOverview.Build([new(thermometer,State(thermometer.Model))],new([],[]));
        Check(overview.Environment.Single().Temperature is null,"Missing readings are unknown");
        var switchDevice=Device("xiaomi.switch.w2","switch.s2","bath") with{CardCategory="light"};
        DeviceCardState ChannelState(params (int S,int P,object V)[] values)
        {var snapshot=State(switchDevice.Model,values);return snapshot with{Schema=new(snapshot.Schema.Properties.Where(p=>p.Siid==2).ToList())};}
        var switchState=ChannelState((2,1,true),(2,2,0));
        overview=HomeOverview.Build([new(switchDevice,switchState),new(switchDevice with{Id="switch2.s2"},ChannelState((2,2,0)))],new([],[]));
        Check(overview.Lighting.Single() is{On:1,Unknown:1},"Missing light state is not counted as off");
        overview=HomeOverview.Build([new(switchDevice,ChannelState((2,1,true),(2,2,1)))],new([],[]));
        Check(overview.Lighting.Count==0,"Wireless channels do not count as lights");
        var schema=tempState.Schema;
        var binding=new AdapterBinding("temperature",schema.Properties.Single(p=>p.Piid==1001),false,"temperature");
        var adapter=new DeviceAdapter("test.thermometer","示例","1.0.0","temperature-humidity-sensor",[binding],[new("metric","温度","temperature",null,null)],[],[],"");
        var unknown=new DeviceIdentity("unknown","Unknown","vendor.sensor.new","urn:miot-spec-v2:device:temperature-humidity-sensor:0000A00A:vendor:1","home","other",true){RoomId="other",Adapter=adapter};
        overview=HomeOverview.Build([new(unknown,tempState)],new([],[]));
        Check(overview.Environment.Single().Temperature==0,"Plugin capability feeds home overview");
        var power=State("cuco.plug.v3",(2,1,false),(2,3,0)).Schema.Properties.Single(p=>p.Siid==2&&p.Piid==1);
        var outlet=Device("cuco.plug.v3","plug","living");
        var outletState=State(outlet.Model,(2,1,false),(2,3,0));
        var outletAdapter=adapter with{Category="outlet",Bindings=[new("power",power,true,"power")]};
        Check(DeviceExperience.CanWrite(outlet with{Adapter=outletAdapter},outletState,power,true),"Declared compatible plugin control is available");
        Check(!DeviceExperience.CanWrite(outlet with{Adapter=outletAdapter with{Bindings=[new("power",power,false,"power")]}},outletState,power,true),"Read-only adapter cannot write");
        Check(!DeviceExperience.CanWrite(outlet with{Adapter=outletAdapter},State(outlet.Model,(2,1,false),(2,3,1)),power,true),"Plugin controls obey device fault guard");
        var textProperty=power with{Type="string",Values=[],Range=[]};
        var textState=new DeviceCardState("",new([textProperty]),JsonSerializer.SerializeToElement(new[]{new{siid=2,piid=1,code=0,value="待机"}}),null,null);
        Check(AdapterReadings.Format(textState,textProperty)=="待机","String status is supported");
        Console.WriteLine($"Passed {checks} targeted home and adapter checks.");
    }
}

// Only the data projection used by HomeOverview; excludes WinUI rendering from offline checks.
namespace MiHome.WinUI.ViewModels
{
    public sealed record DeviceTile(DeviceIdentity Device,DeviceCardState? State=null)
    {
        public string Name=>Device.Name;
        public string Room=>Device.Room;
        public string Summary=>DeviceCardPresentation.Summary(Device,State);
    }
}
