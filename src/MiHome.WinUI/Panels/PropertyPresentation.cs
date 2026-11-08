using System.Globalization;
using System.Text.Json;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Panels;

internal static class PropertyPresentation
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["on"] = Infrastructure.UiText.Get("S92d9be0a0e94"), ["brightness"] = Infrastructure.UiText.Get("S8e2f643ad160"), ["color-temperature"] = Infrastructure.UiText.Get("S84ac609620e5"),
        ["color"] = Infrastructure.UiText.Get("S070a8016fcc3"), ["mode"] = "模式", ["temperature"] = Infrastructure.UiText.Get("S3732e264dfa9"),
        ["relative-humidity"] = Infrastructure.UiText.Get("S9b331be8923c"), ["battery-level"] = Infrastructure.UiText.Get("S1c69b1aa723a"),
        ["power-consumption"] = Infrastructure.UiText.Get("S0035761a7414"), ["electric-power"] = Infrastructure.UiText.Get("S3993e09bcbbe"),
        ["fan-level"] = Infrastructure.UiText.Get("Sd8d84897679e"), ["target-humidity"] = Infrastructure.UiText.Get("Sef4cb5a826ac"),
        ["target-temperature"] = Infrastructure.UiText.Get("Sfd20e18350d5"), ["current-position"] = Infrastructure.UiText.Get("S6be6c8248ec6"),
        ["target-position"] = Infrastructure.UiText.Get("S4a4f9c0beda1"), ["pm2.5-density"] = "PM2.5",
        ["volume"] = Infrastructure.UiText.Get("S8bf8b9780d34"), ["weight"] = Infrastructure.UiText.Get("S38f7c7e22faf"), ["fault"] = Infrastructure.UiText.Get("S5a0cc6c04776"), ["status"] = "状态",
        ["child-lock"] = Infrastructure.UiText.Get("S0f68010bc429"), ["screen-brightness"] = Infrastructure.UiText.Get("S754cd3e2de17"), ["filter-life-level"] = Infrastructure.UiText.Get("S303a06eaded5"),
        ["left-time"] = Infrastructure.UiText.Get("S568c1e74fb06"), ["delay-time"] = Infrastructure.UiText.Get("S4fb3ea271d03"), ["delay-off-countdown"] = Infrastructure.UiText.Get("S1030511860e4"),
        ["illuminance"] = Infrastructure.UiText.Get("Sa30f63625458"), ["power-off-memory"] = Infrastructure.UiText.Get("Sfea4e98eb217"), ["name"] = Infrastructure.UiText.Get("Sd44e9b3d3b31")
    };
    private static readonly HashSet<string> PrimaryProperties = ["on", "brightness", "color-temperature", "mode",
        "fan-level", "target-humidity", "target-temperature", "temperature", "relative-humidity",
        "current-position", "target-position", "pm2.5-density", "battery-level", "volume", "weight"];
    private static string Semantic(string urn) => urn.Split(':').ElementAtOrDefault(3) ?? "";
    public static bool IsPrimary(PropertySchema property) => (property.Readable || property.Writable) &&
        Standard(property.PropertyType) && PrimaryProperties.Contains(Semantic(property.PropertyType));
    private static bool Standard(string urn) => urn.StartsWith("urn:miot-spec-v2:", StringComparison.Ordinal);
    public static string Name(PropertySchema property) => !string.IsNullOrEmpty(property.DisplayName) ? property.DisplayName :
        Standard(property.PropertyType) ? Names.GetValueOrDefault(Semantic(property.PropertyType), property.Name) : property.Name;
    public static string Service(PropertySchema property) => !string.IsNullOrEmpty(property.DisplayService) ? property.DisplayService : !Standard(property.ServiceType) ? property.Service : Semantic(property.ServiceType) switch
    {
        "light" => Infrastructure.UiText.Get("S3b579010a041"), "switch" => Infrastructure.UiText.Get("S92d9be0a0e94"), "outlet" => Infrastructure.UiText.Get("S17cb97521322"), "fan" => Infrastructure.UiText.Get("S3e41bc3fa911"),
        "temperature-humidity-sensor" => Infrastructure.UiText.Get("S55b734e846e0"), "battery" => Infrastructure.UiText.Get("S8a2fd55b0118"), "environment" => Infrastructure.UiText.Get("S904dd029c768"), "filter" => "滤芯", "alarm" => Infrastructure.UiText.Get("Sfdd8ff337f90"), "screen" => Infrastructure.UiText.Get("S04d4aba0ab5f"), "speaker" => Infrastructure.UiText.Get("Sf0c875bd7387"), _ => property.Service
    };
    public static string Unit(string unit) => unit switch
    {
        "none" => "", "percentage" => "%", "celsius" => "°C", "kelvin" => "K",
        "watt" => "W", "volt" => "V", "ampere" => "A", "kWh" => "kWh", "seconds"=>Infrastructure.UiText.Get("S9dcdc2b289b9"), "minutes"=>Infrastructure.UiText.Get("Sbd957bc497aa"), "hours"=>Infrastructure.UiText.Get("S3b6fefc50feb"), _ => unit
    };
    public static string EnumLabel(PropertySchema property,EnumValue option) =>
        Semantic(property.PropertyType) is "playing-state" or "playback-state" or "playback-status"
            ? Infrastructure.UiText.EnumLabel(option.DisplayLabel) : Infrastructure.UiText.State(option.DisplayLabel);
    public static string Value(PropertySchema property, JsonElement value)
    {
        var option = property.Values.FirstOrDefault(v => JsonElement.DeepEquals(v.Value, value));
        if (option is not null) return EnumLabel(property,option);
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return Infrastructure.UiText.Get(value.GetBoolean()?"TurnedOn":"TurnedOff");
        var unit = Unit(property.Unit);
        var text = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number.ToString("G", CultureInfo.CurrentCulture) : value.ToString();
        return string.IsNullOrEmpty(unit) ? text : $"{text} {unit}";
    }
}
