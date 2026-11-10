using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Devices;

internal enum ExperienceKind { Generic, Switch, Outlet, TemperatureHumidity, Occupancy, Humidifier, Light, Router }
internal static class DeviceExperience
{
    public static ExperienceKind Kind(DeviceIdentity device, DeviceSchema? schema = null) => (DeviceModels.Find(device)?.Category ?? DeviceCapabilities.Match(device, schema)?.Category) switch
    {
        "switch" => ExperienceKind.Switch,
        "outlet" => ExperienceKind.Outlet,
        "temperature-humidity-sensor" => ExperienceKind.TemperatureHumidity,
        "occupancy-sensor" => ExperienceKind.Occupancy,
        "humidifier" => ExperienceKind.Humidifier,
        "light" => ExperienceKind.Light,
        "router" => ExperienceKind.Router,
        _ => ExperienceKind.Generic
    };
    public static List<PropertySchema> PowerProperties(DeviceIdentity device, DeviceSchema schema) => PowerReadings(device, schema).Where(p => p.Writable).ToList();
    public static List<PropertySchema> PowerReadings(DeviceIdentity device, DeviceSchema schema)
    {
        var kind = Kind(device, schema);
        var properties = DeviceModels.Find(device) is not null ? schema.Properties : DeviceCapabilities.Match(device, schema)?.Properties ?? [];
        var extraService=DeviceCardPresentation.Semantic(device.SpecType);
        var extra=kind==ExperienceKind.Generic && extraService is "fan" or "air-purifier" or "air-fryer" or "speaker";
        return properties.Where(p => DeviceCardPresentation.Semantic(p.PropertyType) == "on" && p.Readable && p.Type == "bool" &&
            DeviceCardPresentation.Semantic(p.ServiceType) == (kind == ExperienceKind.Humidifier ? "humidifier" : kind == ExperienceKind.Light ? "light" : extra ? extraService : "switch") &&
            (kind is ExperienceKind.Switch or ExperienceKind.Outlet or ExperienceKind.Humidifier or ExperienceKind.Light || extra)).OrderBy(p => p.Siid).ToList();
    }
    public static bool CanWrite(DeviceIdentity device, DeviceCardState? state, PropertySchema property, object? desired)
    {
        if (!device.Online || state is null || !property.Readable || !property.Writable ||
            !DeviceCardPresentation.TryValue(state, property, out var current)) return false;
        if (device.Adapter is { } adapter)
        {
            if (!adapter.Bindings.Any(b => b.AllowWrite && b.Property.Siid==property.Siid && b.Property.Piid==property.Piid)) return false;
            if (property.Type is not ("bool" or "string") && property.Range.Length<2 && property.Values.Count==0) return false;
            if (DeviceModels.Find(device) is null && state.Schema.Properties.Any(p=>p.Siid==property.Siid &&
                DeviceCardPresentation.Semantic(p.ServiceType)=="switch" && DeviceCardPresentation.Semantic(p.PropertyType)=="mode")) return false;
        }
        else if (DeviceModels.Find(device) is null)
        {
            var match = DeviceCapabilities.Match(device, state.Schema);
            if (match is null || !match.Properties.Any(p => p.Siid == property.Siid && p.Piid == property.Piid)) return false;
            if (property.Type != "bool" && property.Values.Count==0 && (property.Range.Length < 2 || !double.IsFinite(property.Range[0]) ||
                !double.IsFinite(property.Range[1]) || property.Range[0] >= property.Range[1])) return false;
            // An unknown outlet's mode must be reviewed before it can expose supply controls.
            if (match.Category == "outlet" && state.Schema.Properties.Any(p => p.Siid == match.Siid && DeviceCardPresentation.Semantic(p.PropertyType) == "mode")) return false;
        }
        var service = DeviceCardPresentation.Semantic(property.ServiceType);
        if(device.Adapter is null && service=="light" && DeviceCardPresentation.Semantic(property.PropertyType) is "brightness" or "color-temperature" or "color")
        {
            var power=state.Schema.Properties.FirstOrDefault(p=>p.Siid==property.Siid&&DeviceCardPresentation.Semantic(p.PropertyType)=="on");
            if(power is null||!DeviceCardPresentation.TryValue(state,power,out var on)||on.ValueKind!=JsonValueKind.True)return false;
        }
        if (DeviceModels.Find(device)?.WirelessMode == true && service == "switch" &&
            DeviceCardPresentation.Number(state, "switch", "mode", property.Siid) != 0) return false;
        if (desired is true || (desired is JsonElement j && j.ValueKind == JsonValueKind.True))
        {
            var fault = state.Schema.Properties.FirstOrDefault(p => p.Siid == property.Siid && DeviceCardPresentation.Semantic(p.PropertyType) == "fault");
            if (fault is not null && (!DeviceCardPresentation.TryValue(state, fault, out var f) || f.ValueKind != JsonValueKind.Number || f.GetDouble() != 0)) return false;
        }
        return true;
    }
    public static bool IsWireless(DeviceIdentity device, DeviceCardState? state, int siid) =>
        DeviceModels.Find(device)?.WirelessMode == true && DeviceCardPresentation.Number(state, "switch", "mode", siid) == 1;
    public static string DefaultSize(DeviceIdentity device) => device.Adapter is not null ? "standard" : (DeviceModels.Find(device)?.Category == "temperature-humidity-sensor" || DeviceCapabilities.CandidateCategory(device) == "temperature-humidity-sensor") ? "standard" : Kind(device) switch
    {
        ExperienceKind.Switch => !device.Id.Contains(".s", StringComparison.Ordinal) || DeviceCardPresentation.Category(device, null) == "light" ? "standard" : "compact",
        ExperienceKind.Humidifier or ExperienceKind.TemperatureHumidity => "standard",
        _ => "compact"
    };
    public static string Size(DeviceIdentity device) => device.CardSize is "compact" or "standard" or "expanded" ? device.CardSize : DefaultSize(device);
    public static CardAppearanceOption[] Sizes(DeviceIdentity device) =>
        [new("", "自动"), new("compact", "紧凑"), new("standard", "标准"), new("expanded", "展开")];
}

internal enum DeviceWriteResult { Confirmed, Rejected, Unknown }

// Shared by device pages, cards and automation. Never retries a write automatically.
internal sealed class DeviceControlSession(DeviceIdentity device, DeviceSchema schema, IDeviceService service)
{
    public IDeviceService Service => service;
    private readonly SemaphoreSlim gate = new(1);
    private (PropertySchema Property, JsonElement Value)? pendingWrite;
    public DeviceCardState? State { get; private set; }
    public bool Busy { get; private set; }
    public string? Feedback { get; private set; }
    public event Action? Changed;
    public event Action<DeviceCardState>? StateRead;
    public void Accept(DeviceCardState state)
    {
        State = state with { DeviceId = device.Id, Schema = schema, Adapter = device.Adapter };
        StateRead?.Invoke(State);
        Changed?.Invoke();
    }
    private async Task ReadCoreAsync()
    {
        var values = await service.ReadAsync(device.Id);
        if (pendingWrite is { } pending)
        {
            var snapshot = new DeviceCardState(device.Id, schema, values, DateTimeOffset.UtcNow, null);
            if (DeviceCardPresentation.TryValue(snapshot, pending.Property, out var actual) && JsonElement.DeepEquals(actual, pending.Value))
            { pendingWrite = null; Feedback = null; }
        }
        else Feedback = null;
        Accept(new(device.Id, schema, values.Clone(), DateTimeOffset.UtcNow, null));
    }
    private void Invalidate()
    {
        Accept(new(device.Id, schema, JsonSerializer.SerializeToElement(Array.Empty<object>()), null, "状态暂不可用"));
    }
    public async Task RefreshAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        Busy = true; Changed?.Invoke();
        try { await ReadCoreAsync(); }
        catch (BridgeException e) when (e.Code == "auth") { Invalidate(); throw; }
        catch { Invalidate(); Feedback = "状态暂时无法更新，请刷新后重试。"; }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }
    public async Task<DeviceWriteResult> WriteAsync(PropertySchema property, object? desired)
    {
        if (!await gate.WaitAsync(0)) return DeviceWriteResult.Rejected;
        Busy = true; Feedback = "正在确认设备状态…"; Changed?.Invoke();
        var sent = false;
        try
        {
            // Re-check channel mode and current values immediately before a user-requested write.
            await ReadCoreAsync();
            if (!DeviceExperience.CanWrite(device, State, property, desired))
            { Feedback = "当前状态不允许此操作，请检查无线模式或刷新状态。"; return DeviceWriteResult.Rejected; }
            var expected = JsonSerializer.SerializeToElement(desired);
            if (DeviceCardPresentation.TryValue(State, property, out var current) && JsonElement.DeepEquals(expected, current))
            { Feedback = null; return DeviceWriteResult.Confirmed; }
            Feedback = "正在应用…"; Changed?.Invoke();
            sent = true;
            pendingWrite = (property, expected);
            await service.WriteAsync(new(device.Id, property.Siid, property.Piid), desired);
            await Task.Delay(800);
            await ReadCoreAsync();
            Feedback = DeviceCardPresentation.TryValue(State, property, out current) && JsonElement.DeepEquals(expected, current)
                ? null : "请求已接受，结果待确认。请刷新状态，不要重复提交。";
            return Feedback is null ? DeviceWriteResult.Confirmed : DeviceWriteResult.Unknown;
        }
        catch (BridgeException e) when (e.Code is "auth" or "invalid")
        { pendingWrite = null; Feedback = e.Message; if (e.Code == "auth") { Invalidate(); throw; } return DeviceWriteResult.Rejected; }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Invalidate();
            Feedback = sent ? "操作结果待确认。请刷新状态，不要重复提交。" : "无法读取设备状态，本次未发送操作。";
            return sent ? DeviceWriteResult.Unknown : DeviceWriteResult.Rejected;
        }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }
}
