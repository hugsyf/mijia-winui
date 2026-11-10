using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
if(args.Contains("--updates-locales")){await UpdateAndLocaleChecks.RunAsync();return;}
if(args.Contains("--adapter-batch")){AdapterBatchChecks.Run();return;}
if(args.Contains("--expanded-actions")){ExpandedChecks.Run();return;}
if(args.Contains("--labels")){LabelChecks.Run();return;}
if(args.Contains("--sdk-controls")){SdkControlChecks.Run();return;}
if(args.Contains("--documents")){DocumentChecks.Run();return;}
if(args.Contains("--images-settings")){await ImageCacheChecks.RunAsync();return;}
if(args.Contains("--lighting")){await LightingChecks.RunAsync();return;}
if(args.Contains("--home-adapters")){HomeAdapterChecks.Run();return;}
if (args.Contains("--automation")) { await AutomationChecks.RunAsync(); return; }
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "device-card-specs.json"))).RootElement;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
DeviceIdentity Device(string model, string id = "p") => new(id, "示例", model, fixtures.GetProperty(model).GetProperty("specType").GetString()!, "h", "房间", true);
DeviceCardState State(string model, params (int Siid, int Piid, object Value)[] values) => new("p",
    fixtures.GetProperty(model).Deserialize<DeviceSchema>(options)!,
    JsonSerializer.SerializeToElement(values.Select(v => new { siid = v.Siid, piid = v.Piid, code = 0, value = v.Value })), DateTimeOffset.UtcNow, null);
foreach (var model in DeviceCardPresentation.AdaptedModels)
    Require(DeviceCardPresentation.Summary(Device(model), null) == "状态暂不可用", model + " must not invent values");
var sw = Device("xiaomi.switch.w2");
Require(DeviceCardSizing.FromDrag("compact", 280, 0, 70, 3) == "standard", "Drag downward to a standard card");
Require(DeviceCardSizing.FromDrag("standard", 280, 160, 0, 3) == "expanded", "Drag right to span two columns");
Require(DeviceCardSizing.FromDrag("standard", 280, 0, -90, 3) == "compact", "Drag upward to shrink");
Require(DeviceCardSizing.FromDrag("expanded", 280, -300, 0, 3) == "standard", "Shrink an expanded card to one column");
Require(DeviceCardSizing.FromDrag("compact", 280, 1000, 0, 1) == "compact", "One-column windows cannot request a second column by dragging");
var left = sw with { Id = "p.s2", Name = "灯", RoomId = "living" };
var right = sw with { Id = "p.s3", Name = "排气扇", RoomId = "bathroom" };
Require(DeviceListing.Visible([sw, left, right]).SequenceEqual([left, right]), "Two complete channels replace their physical switch");
Require(DeviceListing.Visible([sw, left]).Contains(sw), "A missing channel keeps the parent available");
Require(DeviceListing.Visible([sw, left, right with { HomeId = "other" }]).Contains(sw), "Another home cannot hide a parent");
Require(DeviceListing.Visible([sw, left, right with { Model = "other.model" }]).Contains(sw), "Another model cannot hide a parent");
Require(DeviceListing.Parent(left, [sw, left, right]) == sw, "A channel can open physical switch settings");
Require(DeviceListing.Parent(sw with { Id = "p.s20" }, [sw]) is null, "Do not confuse s20 and s2");
Require(DeviceListing.Visible([left, right]).Count == 2, "Orphan channels remain visible");
Require(DeviceListing.Visible([sw, left, right]).Where(d => d.RoomId == "living").Single() == left, "Project before room filtering");
var mixed = State(sw.Model, (2, 1, false), (2, 2, 0), (3, 1, true), (3, 2, 1), (7, 1, true));
Require(DeviceCardPresentation.Summary(sw, mixed) == "左键 已关闭 · 右键 无线模式", "Parent switch must distinguish relays and wireless mode");
var childState = mixed with { Schema = new(mixed.Schema.Properties.Where(p => p.Siid == 3).ToList()) };
Require(DeviceCardPresentation.Category(sw with { Id = "p.s3", CardCategory = "light" }, childState) == "wireless-switch", "Wireless channel must override light appearance");
Require(DeviceCardPresentation.Summary(sw, childState) == "无线模式", "A split channel must not show another relay");
var plug = Device("cuco.plug.v3");
Require(DeviceCardPresentation.Summary(plug, State(plug.Model, (2, 1, false), (11, 2, 0))) == "已断电 · 0 W", "False and zero are valid readings");
var bad = State(plug.Model, (2, 1, "false"), (11, 2, -1));
Require(DeviceCardPresentation.Summary(plug, bad) == "状态暂不可用", "Reject wrong type and out-of-range values");
var occupancy = Device("linp.sensor_occupy.es2");
Require(DeviceCardPresentation.Summary(occupancy, State(occupancy.Model, (2, 1078, 255))) == "有人", "Occupancy is an enum, not a bool");
Require(DeviceCardPresentation.Summary(occupancy, State(occupancy.Model, (2, 1078, 0))) == "无人", "Only the defined zero means unoccupied");
var humidifier = Device("xiaomi.humidifier.600");
Require(DeviceCardPresentation.Summary(humidifier, State(humidifier.Model, (2, 1, true), (2, 2, 4))) == "缺水", "Fault must take priority over on state");
var camera = Device("chuangmi.camera.81ac1");
Require(DeviceCardPresentation.Summary(camera, State(camera.Model, (3, 1, true))) == "状态暂不可用", "Camera indicator must not masquerade as camera power");
var router = Device("xiaomi.router.rn07");
Require(DeviceCardPresentation.Summary(router, State(router.Model, (2, 3, 3))) == "3 台设备已连接", "Router must not invent rate units");
Require(DeviceCardPresentation.Summary(plug with { Online = false }, State(plug.Model, (2, 1, true))) == "", "Offline cards must not show stale readings");
Console.WriteLine("PASS: 10 missing-state fallbacks, switch channel/mode semantics, valid false/zero, malformed data, occupancy enums, fault priority, camera indicator isolation, router count, offline suppression");

Require(DeviceExperience.Size(sw) == DeviceExperience.Size(sw with { CardCategory = "light" }), "State/category changes must not resize cards");
Require(DeviceExperience.Size(sw with { CardSize = "expanded" }) == "expanded", "Persist explicit expanded layout");
Require(DeviceExperience.Size(sw with { Id = "p.s3" }) == "compact", "A single channel defaults to a compact card");
Require(DeviceExperience.Size(plug) == "compact", "An outlet defaults to a compact card");
Require(DeviceExperience.Size(plug with { CardSize = "standard" }) == "standard", "Keep user-selected standard layout");
Require(!DeviceExperience.CanWrite(sw, childState, childState.Schema.Properties.First(p => p.Piid == 1), true), "A wireless relay cannot be quick-controlled");
var plugState = State(plug.Model, (2, 1, false), (2, 3, 0), (11, 2, 0));
var power = plugState.Schema.Properties.First(p => p.Siid == 2 && p.Piid == 1);
var service = new FakeService(plugState.Values);
var session = new DeviceControlSession(plug, plugState.Schema, service);
await session.WriteAsync(power, true);
Require(service.Writes == 1 && session.Feedback is null, "A confirmed operation is written once");
Require(service.LastKey?.DeviceId == plug.Id && service.LastKey.Siid == 2, "Writes preserve actual device identity and service");
await session.WriteAsync(power, true);
Require(service.Writes == 1, "An already satisfied value is not written again");
service.Values = plugState.Values;
service.FailWrite = true;
await session.WriteAsync(power, true);
Require(service.Writes == 2 && session.Feedback!.Contains("待确认"), "An ambiguous write is never retried");
service.Values = State(plug.Model, (2, 1, true), (2, 3, 0)).Values;
await session.RefreshAsync();
Require(service.Writes == 2 && session.Feedback is null, "Manual read confirms the pending result without another write");
var wirelessService = new FakeService(childState.Values);
var wirelessSession = new DeviceControlSession(sw with { Id = "p.s3" }, childState.Schema, wirelessService);
await wirelessSession.WriteAsync(childState.Schema.Properties.First(p => p.Piid == 1), false);
Require(wirelessService.Writes == 0, "Fresh wireless mode prevents writes even if a cached card was clickable");
Console.WriteLine("PASS: stable sizes, offline/wireless guards, confirmed writes, exact targets, already-set values, ambiguous results and later read confirmation");

// Verified model families must retain exact channel identity and mode semantics.
foreach (var model in new[] { "xiaomi.switch.w1", "xiaomi.switch.w3", "zimi.switch.dhkg01", "zimi.switch.dhkg02", "zimi.switch.dhkg05" })
{
    var device = Device(model);
    var channels = DeviceModels.Find(device)!.Channels!;
    var children = channels.Select(s => device with { Id = $"p.s{s}" }).ToList();
    Require(DeviceListing.Visible(children.Prepend(device)).SequenceEqual(children), model + " complete channels replace parent");
    Require(DeviceListing.Visible(children.Skip(1).Prepend(device)).Contains(device), model + " incomplete channels keep parent");
    foreach (var siid in channels)
    {
        var state = State(model, (siid, 1, false), (siid, 2, 0), (2, 3, 0));
        state = state with { Schema = new(state.Schema.Properties.Where(p => p.Siid == siid).ToList()) };
        var child = device with { Id = $"p.s{siid}" };
        var relay = DeviceExperience.PowerProperties(child, state.Schema).Single();
        Require(relay.Siid == siid && DeviceExperience.CanWrite(child, state, relay, true), model + " wired relay enabled");
        Require(DeviceListing.Parent(child, [device]) == device, model + " exact physical parent");
        var wireless = State(model, (siid, 1, true), (siid, 2, 1)) with { Schema = state.Schema };
        Require(!DeviceExperience.CanWrite(child, wireless, relay, false), model + " wireless relay blocked");
        Require(DeviceCardPresentation.Category(child, wireless) == "wireless-switch", model + " wireless appearance");
        var unknownMode = State(model, (siid, 1, true)) with { Schema = state.Schema };
        Require(!DeviceExperience.CanWrite(child, unknownMode, relay, false), model + " unknown mode blocked");
    }
}
var triple = Device("xiaomi.switch.w3");
Require(DeviceModels.ChannelLabel(triple, 2) == "左键" && DeviceModels.ChannelLabel(triple, 3) == "中键" && DeviceModels.ChannelLabel(triple, 4) == "右键", "Three distinct channel labels");
var inching = Device("giot.switch.v6oodm");
var inchState = State(inching.Model, (2, 1, false), (2, 5, 1));
Require(!DeviceExperience.IsWireless(inching, inchState, 2) && DeviceExperience.CanWrite(inching, inchState, DeviceExperience.PowerProperties(inching, inchState.Schema).Single(), true), "Inching mode is not wireless mode");
foreach (var device in DeviceModels.Profiles.Where(p => p.Value.Category == "light").Select(p => Device(p.Key)))
{
    var state = State(device.Model, (2, 1, false), (2, 2, 50));
    Require(DeviceExperience.PowerProperties(device, state.Schema).Single().Siid == 2, "Only actual light power");
    Require(DeviceCardPresentation.Summary(device, state) == "已关闭 · 亮度 50%", "Native light status and brightness");
}
Require(DeviceCardPresentation.Summary(Device("isa.magnet.dw2hl"), State("isa.magnet.dw2hl", (2, 2, true))) == "已关闭", "Contact true means closed");
Require(DeviceCardPresentation.Summary(Device("isa.magnet.dw2hl"), State("isa.magnet.dw2hl", (2, 2, false))) == "已打开", "Contact false means open");
Require(DeviceCardPresentation.Number(State("isa.magnet.dw2hl", (2, 2, false)), "magnet-sensor", "contact-state") is null, "Boolean readings are not numeric");
Require(DeviceCardPresentation.Summary(Device("linp.magnet.m1"), State("linp.magnet.m1", (2, 1004, 2))) == "已关闭", "Magnet enum differs from contact bool");
Require(DeviceCardPresentation.Presence(State("xiaomi.sensor_occupy.03", (2, 1078, 2))) == "快速检测中", "Quick Check is not occupancy");
Require(DeviceCardPresentation.Summary(Device("yunmi.kettle.v19"), State("yunmi.kettle.v19", (2, 1, 4), (2, 3, 55))) == "保温中 · 55 °C", "Kettle status and temperature");
Require(DeviceCardPresentation.Summary(Device("lumi.curtain.hmcn02"), State("lumi.curtain.hmcn02", (2, 3, 0), (2, 6, 0))) == "已停止 · 开合 0%", "Curtain zero position remains visible");
Console.WriteLine($"PASS: {DeviceModels.Profiles.Count} model registry; single/double/triple relay projection, exact channels, wireless/inching guards, light controls and additional sensor summaries");

var capabilityFixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "capability-device-specs.json"))).RootElement;
DeviceIdentity AutoDevice(string model) => new("unknown-device", "示例", model, capabilityFixtures.GetProperty(model).GetProperty("specType").GetString()!, "h", "房间", true);
DeviceSchema AutoSchema(string model) => capabilityFixtures.GetProperty(model).Deserialize<DeviceSchema>(options)!;
DeviceCardState AutoState(string model, params (int Siid, int Piid, object Value)[] values) => new("unknown-device", AutoSchema(model),
    JsonSerializer.SerializeToElement(values.Select(v => new { siid=v.Siid, piid=v.Piid, code=0, value=v.Value })), DateTimeOffset.UtcNow, null);
var autoLight = AutoDevice("example.light.unlisted");
var lightSchema = AutoSchema(autoLight.Model);
var lightState = AutoState(autoLight.Model, (17,41,false), (17,42,50), (17,43,3000));
Require(DeviceCardPresentation.CanReadCard(autoLight), "Unlisted candidate enters the initial card read pipeline");
Require(DeviceExperience.Kind(autoLight, lightSchema)==ExperienceKind.Light, "Unlisted lamp matches by actual capabilities");
Require(DeviceCardPresentation.Summary(autoLight, lightState)=="已关闭 · 亮度 50%", "Unlisted lamp uses standard summary");
var actualRelay = DeviceExperience.PowerProperties(autoLight, lightSchema).Single();
Require(actualRelay.Siid==17 && actualRelay.Piid==41, "Never assume service 2/property 1");
var autoService = new FakeService(lightState.Values);
await new DeviceControlSession(autoLight, lightSchema, autoService).WriteAsync(actualRelay,true);
Require(autoService.LastKey is {Siid:17,Piid:41} && autoService.LastKey.DeviceId==autoLight.Id && autoService.Writes==1, "Capability writes use exact device and discovered property");
var basicLight = lightSchema with { Properties=lightSchema.Properties.Where(p=>p.Piid==41).ToList() };
Require(DeviceCapabilities.Match(autoLight,basicLight)?.Properties.Count==1, "Missing optional brightness/temperature keeps basic light");
var readOnly = basicLight with { Properties=basicLight.Properties.Select(p=>p with {Writable=false}).ToList() };
Require(DeviceExperience.Kind(autoLight,readOnly)==ExperienceKind.Light && DeviceExperience.PowerProperties(autoLight,readOnly).Count==0 && DeviceExperience.PowerReadings(autoLight,readOnly).Count==1, "Readable lamp can have no supply control");
var badType = basicLight with { Properties=basicLight.Properties.Select(p=>p with {Type="uint8"}).ToList() };
Require(DeviceCapabilities.Match(autoLight,badType) is null, "Integer on is not bool power");
var ambiguous = new DeviceSchema(basicLight.Properties.Concat(basicLight.Properties.Select(p=>p with {Siid=18})).ToList());
Require(DeviceCapabilities.Match(autoLight,ambiguous) is null, "Ambiguous multiple services fall back");
Require(DeviceCapabilities.Match(autoLight with {SpecType=Device("xiaomi.switch.w1").SpecType},lightSchema) is null, "Do not use an indicator as the main device");
var autoSensor = AutoDevice("example.sensor.unlisted");
var sensorSchema = AutoSchema(autoSensor.Model);
Require(DeviceExperience.Kind(autoSensor,sensorSchema)==ExperienceKind.TemperatureHumidity, "Unlisted thermometer matches numeric types and units");
Require(DeviceCardPresentation.Summary(autoSensor,AutoState(autoSensor.Model,(19,41,0),(19,42,0)))=="0 °C · 0%", "Valid zero readings are retained");
Require(DeviceCapabilities.Match(autoSensor,new(sensorSchema.Properties.Select(p=>p with {Unit="fahrenheit"}).ToList())) is null, "Do not label Fahrenheit as Celsius");
Require(DeviceCapabilities.Match(autoSensor,new(sensorSchema.Properties.Select(p=>p.Piid==42?p with {Siid=20}:p).ToList())) is null, "Temperature/humidity must belong to the same service");
var autoOutlet = AutoDevice("example.plug.unlisted");
var outletState = AutoState(autoOutlet.Model,(23,41,false));
var outletRelay = DeviceExperience.PowerProperties(autoOutlet,outletState.Schema).Single();
Require(DeviceExperience.CanWrite(autoOutlet,outletState,outletRelay,true), "Unlisted basic outlet can use its verified boolean relay");
var modeSource = Device("xiaomi.switch.w1");
var modeMetadata = State(modeSource.Model).Schema.Properties.First(p=>p.Siid==2&&p.Piid==2) with {Siid=23,Piid=90,Readable=false};
var guardedSchema = new DeviceSchema(outletState.Schema.Properties.Append(modeMetadata).ToList());
Require(!DeviceExperience.CanWrite(autoOutlet,outletState with {Schema=guardedSchema},outletRelay,true), "Unreadable unknown outlet mode cannot disappear from guards");
var invalidBrightness = lightSchema.Properties.First(p=>p.Piid==42) with {Unit="unknown"};
Require(!DeviceCapabilities.Match(autoLight,new(lightSchema.Properties.Select(p=>p.Piid==42?invalidBrightness:p).ToList()))!.Properties.Any(p=>p.Piid==42), "Wrong optional units are excluded");
Require(DeviceExperience.Kind(sw with {SpecType=autoLight.SpecType},lightSchema)==ExperienceKind.Switch, "Exact model exceptions outrank templates");
Console.WriteLine("PASS: unlisted light/thermometer/outlet templates, exact discovered routes, optional/read-only properties, type/unit checks, ambiguity, mode guards, and model priority");

Require(DeviceOrdering.Move(["a","hidden","b","c"],"c","a",false).SequenceEqual(["c","a","hidden","b"]), "Filtered drag preserves hidden item ordering");
Require(DeviceOrdering.Move(["a","b","c"],"a","b",true).SequenceEqual(["b","a","c"]), "Drop after a target");
Require(DeviceOrdering.Move(["a","b"],"unknown","b",true).SequenceEqual(["a","b"]), "Reject unknown drag identity");
Require(DeviceOrdering.Apply([sw,plug], [plug.Id,sw.Id]).Count==2, "Ordering tolerates repeated identities without duplicate dictionary keys");
Require(!DrawerLayout.Inline(600,"inline",false) && DrawerLayout.Width(360,false)==360, "Forced inline falls back on narrow windows");
Require(!DrawerLayout.Inline(1100,"auto",false) && !DrawerLayout.Inline(1000,"auto",false), "Details remain overlay at all widths");
Require(!DrawerLayout.Inline(1200,"overlay",false) && !DrawerLayout.Inline(1200,"inline",true) && DrawerLayout.Width(1200,true)==1200, "Overlay and expanded details");
var speedState=State("xiaomi.router.rd03",(2,1,6343037),(2,2,0),(2,3,12));
Require(DeviceCardPresentation.RouterSpeed(speedState,"download-speed")=="6.343 MB/s" && DeviceCardPresentation.RouterSpeed(speedState,"upload-speed")=="0 MB/s", "Router speeds use decimal bytes per second with valid zero");
Require(DeviceCardPresentation.RouterSpeed(State("xiaomi.router.rd03",(2,1,-1)),"download-speed") is null, "Reject invalid negative speed");
var preferenceDirectory=Path.Combine(Path.GetTempPath(),"mihome-preferences-"+Guid.NewGuid().ToString("N"));
try
{
    var preferences=new MiHome.WinUI.Infrastructure.AppPreferences(2,0,false,"overlay",0);
    preferences.Save(preferenceDirectory);
    Require(MiHome.WinUI.Infrastructure.AppPreferences.Load(preferenceDirectory)==preferences,"Appearance, drawer and refresh preferences persist together");
    File.WriteAllText(Path.Combine(preferenceDirectory,"settings.json"),"{\"theme\":1}");
    Require(MiHome.WinUI.Infrastructure.AppPreferences.Load(preferenceDirectory).Theme==1,"Migrate old theme-only preferences");
}
finally
{
    if (Directory.Exists(preferenceDirectory)) { foreach(var file in Directory.GetFiles(preferenceDirectory)) File.Delete(file); Directory.Delete(preferenceDirectory); }
}
Console.WriteLine("PASS: reorder identity and filtered stability, drawer breakpoints/full mode, decimal router rates, preference persistence and migration");


var existingA = new object(); var existingB = new object(); var existingC = new object();
var stable = new System.Collections.ObjectModel.ObservableCollection<object>([existingA,existingB,existingC]);
var resetCount=0; var moveCount=0; var replaceCount=0;
stable.CollectionChanged += (_,e) => { if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset)resetCount++;
    if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Move)moveCount++;
    if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Replace)replaceCount++; };
string StableKey(object x) => ReferenceEquals(x,existingA)?"a":ReferenceEquals(x,existingB)?"b":"c";
MiHome.WinUI.ViewModels.CollectionReconciler.Apply(stable,new[]{existingC,existingA,existingB},StableKey);
Require(resetCount==0 && replaceCount==0 && moveCount==1 && ReferenceEquals(stable[0],existingC), "Reordering reuses objects with one Move, no reset/replacement");
MiHome.WinUI.ViewModels.CollectionReconciler.Apply(stable,new[]{existingC,existingB},StableKey);
Require(resetCount==0 && ReferenceEquals(stable[1],existingB), "Filtering preserves retained object identity");
Console.WriteLine("PASS: stable collection moves without resets or item replacement");

sealed class FakeService(JsonElement values) : IDeviceService
{
    public JsonElement Values { get; set; } = values;
    public int Writes { get; private set; }
    public bool FailWrite { get; set; }
    public PropertyKey? LastKey { get; private set; }
    public Task<DeviceSchema> GetSchemaAsync(string id) => throw new NotSupportedException();
    public Task<JsonElement> ReadAsync(string id) => Task.FromResult(Values);
    public Task WriteAsync(PropertyKey key, object? value)
    {
        Writes++; LastKey = key;
        if (FailWrite) throw new TimeoutException();
        Values = JsonSerializer.SerializeToElement(Values.EnumerateArray().Select(entry =>
            entry.GetProperty("siid").GetInt32() == key.Siid && entry.GetProperty("piid").GetInt32() == key.Piid
                ? JsonSerializer.SerializeToElement(new { siid = key.Siid, piid = key.Piid, code = 0, value }) : entry));
        return Task.CompletedTask;
    }
}
