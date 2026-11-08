using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;

internal static class LightingChecks
{
    public static async Task RunAsync()
    {
        var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "device-card-specs.json"))).RootElement.GetProperty("xiaomi.switch.w2");
        var schema = fixture.Deserialize<DeviceSchema>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        schema = new(schema.Properties.Where(p => p.Siid == 2).ToList());
        DeviceIdentity Device(string id, string room = "living") => new(id, id, "xiaomi.switch.w2", fixture.GetProperty("specType").GetString()!, "home", room, true) { RoomId = room, CardCategory = "light" };
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var fake = new LightingService(schema);
        var controller = new HomeLightingController(fake);
        DeviceTile Tile(DeviceIdentity device) => new(device, new(device.Id, schema, fake.Values(device.Id), DateTimeOffset.Now, null));
        var good = Device("good.s2"); var pending = Device("pending.s2"); var failed = Device("failed.s2");
        fake.Pending.Add(pending.Id); fake.Failed.Add(failed.Id);
        var results = await controller.ExecuteAsync([Tile(good), Tile(pending), Tile(failed)], null, true);
        Check(results.Count(r => r.Outcome == "confirmed") == 1 && results.Count(r => r.Outcome == "unconfirmed") == 2, "Partial failures and pending writes are not reported as success");
        Check(fake.Writes.Values.Sum() == 3, "Each requested write is sent at most once");
        var previous = fake.Writes[good.Id];
        results = await controller.ExecuteAsync([Tile(good)], null, true);
        Check(results.Single().Outcome == "confirmed" && fake.Writes[good.Id] == previous, "Already matching state does not send another write");

        var wireless = Device("wireless.s2"); var offline = Device("offline.s2") with { Online = false };
        var unknown = Device("unknown.s2"); var readOnly = Device("readonly.s2"); var fault = Device("fault.s2");
        var cachedWireless = Tile(wireless);
        fake.Wireless.Add(wireless.Id); fake.Missing.Add(unknown.Id);
        fake.Schemas[readOnly.Id] = new(schema.Properties.Select(p => p.Piid == 1 ? p with { Writable = false } : p).ToList());
        fake.Faults.Add(fault.Id);
        results = await controller.ExecuteAsync([cachedWireless, Tile(offline), Tile(unknown), Tile(readOnly), Tile(fault)], null, true);
        Check(results.Count == 5 && results.All(r => r.Outcome == "skipped"), "Fresh wireless mode, offline, missing state, readonly and fault guards block group writes: "+JsonSerializer.Serialize(results));
        Check(!new[] { wireless.Id, offline.Id, unknown.Id, readOnly.Id, fault.Id }.Any(fake.Writes.ContainsKey), "Blocked targets issue no writes");

        var fan = Device("fan.s2") with { CardCategory = "fan" }; var bedroom = Device("bedroom.s2", "bedroom");
        results = await controller.ExecuteAsync([Tile(fan), Tile(bedroom)], "living", false);
        Check(results.Count == 0 && !fake.Writes.ContainsKey(fan.Id) && !fake.Writes.ContainsKey(bedroom.Id), "Room scope and device purposes exclude other lights and fans");
        var parent = Device("parent"); var child = Device("parent.s2");
        results = await controller.ExecuteAsync([Tile(parent), Tile(child)], null, true);
        Check(results.Count == 1 && fake.Writes.GetValueOrDefault(parent.Id) == 1 && !fake.Writes.ContainsKey(child.Id), "Physical relay aliases are controlled only once");
        fake.AuthFailure = "auth.s2";
        try { await controller.ExecuteAsync([Tile(Device("auth.s2")), Tile(Device("after-auth.s2"))], null, true); throw new Exception("Authentication failure was ignored"); }
        catch (BridgeException error) when (error.Code == "auth") { }
        Check(!fake.Writes.ContainsKey("after-auth.s2"), "Authentication failure stops the remaining batch");
        Console.WriteLine($"Passed {checks} targeted lighting checks; fake device service only.");
    }

    private sealed class LightingService(DeviceSchema defaultSchema) : IDeviceService
    {
        public Dictionary<string, DeviceSchema> Schemas { get; } = [];
        public Dictionary<string, int> Writes { get; } = [];
        public HashSet<string> Pending { get; } = [];
        public HashSet<string> Failed { get; } = [];
        public HashSet<string> Wireless { get; } = [];
        public HashSet<string> Missing { get; } = [];
        public HashSet<string> Faults { get; } = [];
        public string? AuthFailure { get; set; }
        private readonly Dictionary<string, bool> power = [];
        public JsonElement Values(string id) => JsonSerializer.SerializeToElement((Schemas.GetValueOrDefault(id) ?? defaultSchema).Properties
            .Where(p => !Missing.Contains(id) || p.Piid != 1).Select(p => new
            {
                siid = p.Siid, piid = p.Piid, code = 0,
                value = p.Type == "bool" ? (object)power.GetValueOrDefault(id) : DeviceCardPresentation.Semantic(p.PropertyType) == "fault" ? Faults.Contains(id) ? 1 : 0 : Wireless.Contains(id) ? 1 : 0
            }));
        public Task<DeviceSchema> GetSchemaAsync(string id) => Task.FromResult(Schemas.GetValueOrDefault(id) ?? defaultSchema);
        public Task<JsonElement> ReadAsync(string id) => id == AuthFailure ? throw new BridgeException("auth", "Login expired") : Task.FromResult(Values(id));
        public Task WriteAsync(PropertyKey key, object? value)
        {
            Writes[key.DeviceId] = Writes.GetValueOrDefault(key.DeviceId) + 1;
            if (Failed.Contains(key.DeviceId)) throw new TimeoutException();
            if (!Pending.Contains(key.DeviceId)) power[key.DeviceId] = (bool)value!;
            return Task.CompletedTask;
        }
    }
}
