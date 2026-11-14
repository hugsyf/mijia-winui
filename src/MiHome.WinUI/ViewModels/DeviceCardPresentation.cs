using System.Globalization;
using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
namespace MiHome.WinUI.ViewModels;

public sealed record CardAppearanceOption(string Id, string Label);

internal static class DeviceCardPresentation
{
    public static readonly HashSet<string> AdaptedModels = DeviceModels.Profiles.Keys.ToHashSet();
    public static bool CanReadCard(DeviceIdentity device) => device.Adapter is not null || AdaptedModels.Contains(device.Model) || DeviceCapabilities.CandidateCategory(device) is not null;
    public static readonly CardAppearanceOption[] Appearances = [new("", "跟随米家"), new("light", "灯"),
        new("fan", "风扇 / 排气扇"), new("heater", "取暖器"), new("television", "电视"), new("computer", "电脑"),
        new("outlet", "插座"), new("switch", "开关"), new("appliance", "其他电器")];
    public static string Semantic(string urn) => urn.Split(':').ElementAtOrDefault(3) ?? "";
    public static bool CanChooseAppearance(DeviceIdentity device) => DeviceModels.Find(device)?.PurposeIcons == true || device.Model == "cuco.plug.v3";
    public static readonly string[] ConfiguredIcons = ["downlight", "spotlight", "light-strip", "pendant-light", "ceiling-light", "wall-light",
        "bedside-light", "ambient-light", "exhaust-fan", "cooling-panel", "drying-rack", "desk-light", "floor-light",
        "air-conditioner", "refrigerator", "electric-blanket", "air-purifier", "washer", "aquarium", "microwave", "oven",
        "printer", "induction-cooktop", "rice-cooker", "dishwasher", "kettle", "projector", "range-hood"];
    internal static bool TryValue(DeviceCardState? state, PropertySchema property, out JsonElement value)
    {
        value = default;
        if (state?.Values.ValueKind != JsonValueKind.Array) return false;
        foreach (var entry in state.Values.EnumerateArray())
        {
            if (!entry.TryGetProperty("siid", out var s) || !s.TryGetInt32(out var siid) || siid != property.Siid ||
                !entry.TryGetProperty("piid", out var p) || !p.TryGetInt32(out var piid) || piid != property.Piid ||
                !entry.TryGetProperty("code", out var code) || !code.TryGetInt32(out var c) || c != 0 ||
                !entry.TryGetProperty("value", out value)) continue;
            if (property.Type == "string")
            {
                var textValue=value;
                return value.ValueKind==JsonValueKind.String && (property.Values.Count==0 || property.Values.Any(v=>JsonElement.DeepEquals(v.Value,textValue)));
            }
            if (property.Type == "bool") return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number)) return false;
            if (property.Range.Length >= 2 && (number < property.Range[0] || number > property.Range[1])) return false;
            var candidate = value;
            return property.Values.Count == 0 || property.Values.Any(v => JsonElement.DeepEquals(v.Value, candidate));
        }
        return false;
    }
    internal static (PropertySchema Schema, JsonElement Value)? Reading(DeviceCardState? state, string service, string property, int? siid = null)
    {
        foreach (var p in state?.Schema.Properties ?? [])
            if (Semantic(p.ServiceType) == service && Semantic(p.PropertyType) == property && (siid is null || p.Siid == siid) &&
                TryValue(state, p, out var value)) return (p, value);
        return null;
    }
    internal static double? Number(DeviceCardState? state, string service, string property, int? siid = null) =>
        Reading(state, service, property, siid) is { } reading && reading.Value.ValueKind == JsonValueKind.Number ? reading.Value.GetDouble() : null;
    internal static bool? Power(DeviceCardState? state, string service, int? siid = null) =>
        Reading(state, service, "on", siid) is { } reading && reading.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? reading.Value.GetBoolean() : null;
    private static string Numeric(double? value, string suffix, string prefix = "") => value is { } n
        ? prefix + n.ToString("0.#", CultureInfo.CurrentCulture) + suffix : "";
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => p.Length > 0));
    public static string Category(DeviceIdentity device, DeviceCardState? state)
    {
        if (DeviceModels.Find(device)?.WirelessMode == true && state is not null &&
            state.Schema.Properties.Where(p => Semantic(p.ServiceType) == "switch" && Semantic(p.PropertyType) == "mode").ToList() is { Count: > 0 } modes &&
            modes.All(p => TryValue(state, p, out var value) && value.ValueKind == JsonValueKind.Number && value.GetDouble() == 1)) return "wireless-switch";
        if (CanChooseAppearance(device) && !string.IsNullOrEmpty(device.CardCategory)) return device.CardCategory;
        return device.Adapter?.Category ?? DeviceModels.Find(device)?.Category ?? DeviceCapabilities.Match(device, state?.Schema)?.Category ?? Semantic(device.SpecType);
    }
    public static string CategoryName(string category) => category switch
    {
        "light" => "灯", "switch" => "智能开关", "wireless-switch" => "无线开关", "outlet" => "智能插座",
        "fan" => "风扇", "heater" => "取暖器", "television" => "电视", "computer" => "电脑", "appliance" => "电器",
        "camera" => "摄像机", "temperature-humidity-sensor" => "温湿度", "occupancy-sensor" => "人体存在",
        "drying-rack" => "晾衣架", "curtain" => "窗帘", "air-conditioner" => "空调", "refrigerator" => "冰箱",
        "electric-blanket" => "电热毯", "air-purifier" => "空气净化器", "washer" => "洗衣机", "aquarium" => "鱼缸",
        "microwave" => "微波炉", "oven" => "烤箱", "printer" => "打印机", "induction-cooktop" => "电磁炉",
        "rice-cooker" => "电饭煲", "dishwasher" => "洗碗机", "kettle" => "电水壶", "projector" => "投影仪", "range-hood" => "油烟机",
        "magnet-sensor" => "门窗传感器", "speaker" => "音箱", "humidifier" => "加湿器", "toothbrush" => "电动牙刷", "air-fryer" => "空气炸锅", "router" => "路由器", _ => "设备"
    };
    // 24 DIP outline geometry, rendered by WinUI Path with the theme accent brush.
    public static string IconPath(string category) => category switch
    {
        "downlight" => "M6,5 L18,5 L21,16 Q12,22 3,16 Z M3,16 Q12,11 21,16",
        "spotlight" => "M10,3 L16,3 M13,3 L13,7 M10,7 L17,11 L12,20 L5,16 Z M5,16 L12,20",
        "light-strip" => "M20,19 L21,13 A9,9 0 1 0 12,22 M18,13 A6,6 0 1 0 12,19 A3,3 0 1 0 9,16",
        "pendant-light" => "M12,2 L12,7 M8,7 L16,7 L21,18 Q12,23 3,18 Z M5,18 L19,18",
        "ceiling-light" => "M10,3 L14,3 L14,6 M12,6 L12,8 M3,14 Q4,8 12,8 Q20,8 21,14 Q12,20 3,14 Z M3,14 Q12,11 21,14",
        "wall-light" => "M20,3 L20,10 M20,6 L12,6 Q8,6 8,10 M4,10 L12,10 L16,17 Q8,21 1,17 Z",
        "bedside-light" => "M7,3 L17,3 L20,13 L4,13 Z M12,13 L12,21 M7,21 L17,21",
        "floor-light" => "M8,2 L16,2 L17,8 L7,8 Z M12,8 L12,22 M7,22 L17,22",
        "desk-light" => "M11,3 L18,7 L15,13 L7,8 Z M15,12 L18,18 L12,21 M6,22 L19,22",
        "ambient-light" => "M8,3 L16,3 Q19,3 19,7 L19,18 Q19,21 16,21 L8,21 Q5,21 5,18 L5,7 Q5,3 8,3 Z M5,16 L19,16 M11,19 L13,19",
        "exhaust-fan" => "M2,2 L22,2 L22,22 L2,22 Z M12,5 A7,7 0 1 1 11.99,5 M12,12 Q5,3 6,11 Q7,14 12,12 M12,12 Q21,5 13,6 Q10,7 12,12 M12,12 Q19,21 18,13 Q17,10 12,12 M12,12 Q3,19 11,18 Q14,17 12,12",
        "cooling-panel" => "M3,3 L21,3 L21,20 L3,20 Z M6,7 L18,7 M6,11 L18,11 M6,15 L18,15 M9,23 L15,23",
        "drying-rack" => "M4,2 L20,2 M7,2 L7,17 M17,2 L17,17 M3,17 L21,17 L21,21 L3,21 Z M7,17 L7,21 M12,17 L12,21 M17,17 L17,21",
        "air-conditioner" => "M3,4 L21,4 L21,14 L3,14 Z M5,10 L19,10 M7,14 L7,19 M12,14 L12,22 M17,14 L17,19",
        "refrigerator" => "M5,2 L19,2 L19,22 L5,22 Z M5,9 L19,9 M8,5 L8,7 M8,12 L8,16",
        "electric-blanket" => "M3,4 L19,4 L21,20 L5,20 Z M7,8 L15,8 M8,12 L16,12 M9,16 L17,16 M3,4 L2,17 L5,20",
        "air-purifier" => "M7,2 L17,2 L19,22 L5,22 Z M12,5 A3,3 0 1 1 11.99,5 M8,15 L16,15 M8,18 L16,18",
        "washer" => "M4,2 L20,2 L20,22 L4,22 Z M4,7 L20,7 M7,4 L8,4 M12,10 A5,5 0 1 1 11.99,10 M8,16 Q12,13 16,16",
        "aquarium" => "M2,4 L22,4 L22,21 L2,21 Z M2,8 Q7,5 12,8 Q17,11 22,8 M8,14 Q12,10 16,14 Q12,18 8,14 L6,12 L6,16 Z",
        "microwave" => "M2,5 L22,5 L22,20 L2,20 Z M5,8 L16,8 L16,17 L5,17 Z M19,9 L19,10 M19,14 L19,16",
        "oven" => "M3,2 L21,2 L21,22 L3,22 Z M3,7 L21,7 M7,4 L8,4 M16,4 L17,4 M6,10 L18,10 L18,19 L6,19 Z M8,12 L16,12",
        "printer" => "M6,8 L6,2 L18,2 L18,8 M6,18 L3,18 L3,8 L21,8 L21,18 L18,18 M6,14 L18,14 L18,22 L6,22 Z M17,11 L18,11",
        "induction-cooktop" => "M3,5 Q12,1 21,5 L21,19 Q12,23 3,19 Z M12,5 A6,4 0 1 1 11.99,5 M10,19 L14,19",
        "rice-cooker" => "M6,5 L18,5 Q21,5 21,9 L21,18 Q21,22 18,22 L6,22 Q3,22 3,18 L3,9 Q3,5 6,5 Z M9,5 L9,2 L15,2 L15,5 M9,12 L15,12 L15,16 L9,16 Z",
        "dishwasher" => "M3,2 L21,2 L21,22 L3,22 Z M3,7 L21,7 M8,4 L16,4 M7,11 L7,18 M12,11 L12,18 M17,11 L17,18",
        "kettle" => "M6,5 L16,5 L18,21 L4,21 Z M8,5 L8,2 L14,2 L14,5 M17,8 Q24,8 21,15 L18,17 M5,9 L2,6 L6,6",
        "projector" => "M3,7 L21,7 L21,20 L3,20 Z M15,9 A4,4 0 1 1 14.99,9 M6,11 L8,11 M6,15 L8,15 M5,20 L5,22 M19,20 L19,22",
        "range-hood" => "M8,2 L16,2 L16,14 L22,18 L22,21 L2,21 L2,18 L8,14 Z M2,18 L22,18",
        "light" => "M9,18 L15,18 M10,21 L14,21 M8,13 C3,8 7,3 12,3 C17,3 21,8 16,13 L15,16 L9,16 Z",
        "switch" => "M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M12,6 L12,18",
        "wireless-switch" => "M5,12 L19,12 L19,21 L5,21 Z M8,8 Q12,4 16,8 M5,5 Q12,-1 19,5",
        "outlet" => "M6,2 L18,2 Q20,2 20,4 L20,20 Q20,22 18,22 L6,22 Q4,22 4,20 L4,4 Q4,2 6,2 Z M9,7 L9,11 M15,7 L15,11 M10,16 L14,16",
        "camera" => "M5,7 L16,7 Q18,7 18,9 L18,16 Q18,18 16,18 L5,18 Q3,18 3,16 L3,9 Q3,7 5,7 Z M18,10 L22,8 L22,17 L18,15 M7,4 L13,4",
        "temperature-humidity-sensor" => "M10,4 A2,2 0 0 1 14,4 L14,14 A5,5 0 1 1 10,14 Z M12,8 L12,17 M17,6 L20,6 M17,10 L20,10",
        "occupancy-sensor" => "M12,3 A3,3 0 1 1 11.99,3 Z M6,21 L6,17 Q6,12 12,12 Q18,12 18,17 L18,21 M3,8 Q0,12 3,16 M21,8 Q24,12 21,16",
        "humidifier" => "M5,10 L19,10 L21,20 Q21,22 19,22 L5,22 Q3,22 3,20 Z M9,7 Q7,5 9,2 M15,7 Q13,5 15,2 M9,18 L15,18",
        "toothbrush" => "M9,3 L15,3 L15,9 L9,9 Z M10,9 L14,9 L14,20 Q12,23 10,20 Z M10,5 L14,5 M10,7 L14,7 M12,14 L12,16",
        "air-fryer" => "M6,3 L18,3 Q21,3 21,7 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,7 Q3,3 6,3 Z M3,10 L21,10 M9,6 L15,6 M9,13 L15,13 L15,16 L9,16 Z",
        "router" => "M3,12 L21,12 L21,21 L3,21 Z M6,12 L4,3 M18,12 L20,3 M6,17 L6,18 M10,17 L10,18",
        "computer" or "television" => "M3,3 L21,3 L21,17 L3,17 Z M12,17 L12,21 M7,21 L17,21",
        "fan" => "M12,10 C4,3 13,0 15,5 C16,8 14,10 12,10 M14,12 C22,6 25,16 19,17 C16,18 14,14 14,12 M11,14 C13,23 3,23 4,17 C5,14 9,13 11,14 M12,10 A2,2 0 1 1 11.99,10 Z",
        "heater" => "M4,6 L20,6 L20,19 L4,19 Z M8,9 L8,16 M12,9 L12,16 M16,9 L16,16 M6,19 L6,22 M18,19 L18,22",
        "curtain" => "M3,3 L21,3 M5,4 L5,21 L11,18 L11,4 M13,4 L13,18 L19,21 L19,4",
        "magnet-sensor" => "M3,3 L13,3 L13,21 L3,21 Z M17,3 L21,3 L21,21 L17,21 Z",
        "speaker" => "M6,2 L18,2 L18,22 L6,22 Z M10,6 L14,6 M12,11 A4,4 0 1 1 11.99,11 Z",
        _ => "M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M8,12 L16,12 M12,8 L12,16"
    };
    internal static string Presence(DeviceCardState? state)
    {
        var reading = Reading(state, "occupancy-sensor", "occupancy-status");
        if (reading is null) return "";
        var number = Number(state, "occupancy-sensor", "occupancy-status");
        var label = reading.Value.Schema.Values.FirstOrDefault(v => JsonElement.DeepEquals(v.Value, reading.Value.Value))?.DisplayLabel;
        return label == "Quick Check" ? "快速检测中" : number is null ? "" : number == 0 ? "无人" : "有人";
    }
    internal static string? RouterSpeed(DeviceCardState? state, string property)
    {
        if (property is not ("download-speed" or "upload-speed")) return null;
        var schema = state?.Schema.Properties.FirstOrDefault(p => p.Readable && !p.Writable && Semantic(p.ServiceType) == "router" &&
            Semantic(p.PropertyType) == property && (p.Type is "float" or "double" || p.Type.StartsWith("uint") || p.Type.StartsWith("int")));
        // The public 0..100 range contradicts observed byte/s readings. This exception is read-only and rate-specific.
        if (schema is null || !TryValue(state, schema with { Range = [] }, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        var bytes = value.GetDouble();
        if (bytes < 0) return null;
        var (divisor, unit) = bytes switch
        {
            >= 1_000_000_000 => (1_000_000_000d, "GB/s"),
            >= 1_000_000 => (1_000_000d, "MB/s"),
            >= 1_000 => (1_000d, "KB/s"),
            _ => (1d, "B/s")
        };
        return (bytes / divisor).ToString("0.###", CultureInfo.CurrentCulture) + " " + unit;
    }
    public static string Summary(DeviceIdentity device, DeviceCardState? state)
    {
        if (!device.Online) return "";
        var capability = DeviceCapabilities.Match(device, state?.Schema);
        if (!AdaptedModels.Contains(device.Model) && capability is null)
            return DeviceCapabilities.CandidateCategory(device) is not null ? "状态暂不可用" : "";
        if (capability is not null && state is not null) state = state with { Schema = new(capability.Properties) };
        var category = DeviceModels.Find(device)?.Category ?? capability?.Category;
        string summary = "";
        var fault = state?.Schema.Properties.FirstOrDefault(p => Semantic(p.PropertyType) == "fault" &&
            TryValue(state, p, out var v) && v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0);
        if (fault is not null) return device.Model == "xiaomi.humidifier.600" && Number(state, "humidifier", "fault") == 4 ? "缺水" : "设备异常，请查看详情";
        if(device.Adapter is {} adapter)
            return string.Join(" · ",adapter.Card.Where(w=>w.Kind!="group").Select(w=>AdapterLocalizationResolver.Label(adapter,w.LabelKey,w.Label)+" "+AdapterReadings.Format(state,AdapterControlValues.WithLabels(adapter.Bindings.Single(b=>b.Id==w.Binding).Property,AdapterLocalizationResolver.Widget(adapter,w).Options))));
        switch (device.Model)
        {
            case var _ when DeviceModels.IsSwitch(device):
                var channels = state?.Schema.Properties.Where(p => Semantic(p.ServiceType) == "switch" && Semantic(p.PropertyType) == "on").OrderBy(p => p.Siid).ToList() ?? [];
                summary = Join(channels.Select(p =>
                {
                    var prefix = channels.Count > 1 ? DeviceModels.ChannelLabel(device, p.Siid) + " " : "";
                    var mode = Number(state, "switch", "mode", p.Siid);
                    return prefix + (DeviceModels.Find(device)?.WirelessMode == true && mode == 1 ? "无线模式" : (DeviceModels.Find(device)?.WirelessMode != true || mode == 0) && Power(state, "switch", p.Siid) is { } on ? (on ? "已开启" : "已关闭") : "状态待更新");
                }).ToArray());
                break;
            case var _ when category == "outlet":
                summary = Join(Power(state, "switch") is { } plug ? (plug ? "已通电" : "已断电") : "",
                    Numeric(Number(state, "power-consumption", "electric-power"), " W")); break;
            case "chuangmi.camera.81ac1":
                summary = Power(state, "camera-control") is { } camera ? (camera ? "摄像机已开启" : "摄像机已关闭") : ""; break;
            case var _ when category == "temperature-humidity-sensor":
                summary = Join(Numeric(Number(state, "temperature-humidity-sensor", "temperature"), " °C"),
                    Numeric(Number(state, "temperature-humidity-sensor", "relative-humidity"), "%")); break;
            case var _ when DeviceModels.Find(device)?.Category == "occupancy-sensor":
                summary = Join(Presence(state),
                    Numeric(Number(state, "occupancy-sensor", "illumination"), " lx")); break;
            case "xiaomi.humidifier.600":
                var modeName = Number(state, "humidifier", "mode") switch { 0 => "恒湿", 1 => "睡眠", 2 => "强力", 3 => "风干", _ => "" };
                summary = Join(Power(state, "humidifier") is { } humidifier ? (humidifier ? (modeName.Length > 0 ? modeName : "已开启") : "已关闭") : "",
                    Numeric(Number(state, "environment", "relative-humidity"), "%", "湿度 ")); break;
            case "soocare.toothbrush.1501":
                summary = Join(Numeric(Number(state, "battery", "battery-level"), "%", "电量 "),
                    Numeric(Number(state, "brush-head", "brush-head-left-level"), "%", "刷头 ")); break;
            case "xiaomi.fryer.maf16":
                var status = Number(state, "air-fryer", "status");
                var statusText = status switch { 0 => "已关机", 1 => "待机", 2 => "已暂停", 3 => "预约中", 4 => "烹饪中", 6 => "烹饪完成",
                    9 => "翻面暂停", 10 => "保温中", 11 => "保温暂停", 12 => "保温完成", 13 => "速烤中", 14 => "脱脂中", 15 => "嫩烤中", 16 => "精烤中", 17 => "请加水", 18 => "自检中", _ => "" };
                summary = Join(statusText, status is 2 or 4 or 9 or 10 or 11 or 13 or 14 or 15 or 16
                    ? Numeric(Number(state, "air-fryer", "left-time"), " 分钟", "剩余 ") : ""); break;
            case var _ when DeviceModels.Find(device)?.Category == "router":
                summary = Join(Numeric(Number(state, "router", "connected-device-number"), " 台设备已连接"),
                    RouterSpeed(state, "download-speed") is { } download ? "↓ " + download : ""); break;
            case var _ when category == "light":
                summary = Join(Power(state, "light") is { } light ? light ? "已开启" : "已关闭" : "",
                    Numeric(Number(state, "light", "brightness"), "%", "亮度 ")); break;
            case "linp.magnet.m1":
                summary = Number(state, "magnet-sensor", "status") switch { 1 => "已打开", 2 => "已关闭", _ => "" }; break;
            case "isa.magnet.dw2hl":
                summary = Reading(state, "magnet-sensor", "contact-state") is { } contact
                    ? contact.Value.GetBoolean() ? "已关闭" : "已打开" : ""; break;
            case "lumi.curtain.hmcn02":
                summary = Join(Number(state, "curtain", "status") switch { 0 => "已停止", 1 => "正在打开", 2 => "正在关闭", _ => "" },
                    Numeric(Number(state, "curtain", "current-position"), "%", "开合 ")); break;
            case "yunmi.kettle.v19":
                summary = Join(Number(state, "kettle", "status") switch { 0 => "待机", 1 => "加热中", 2 => "煮沸中", 3 => "降温中", 4 => "保温中", _ => "" },
                    Numeric(Number(state, "kettle", "temperature"), " °C")); break;
            case var _ when DeviceModels.Find(device)?.Category == "speaker":
                summary = Number(state, "play-control", "playing-state") switch { 0 => "已停止", 1 => "播放中", 2 => "已暂停", _ => "" }; break;
        }
        return summary.Length > 0 ? summary : "状态暂不可用";
    }
}
