using System.Text.Json;
namespace MiHome.WinUI.Core;
public record Room(string Id, string Name);
public record Home(string Id, string Name, List<Room> Rooms);
public record DeviceIdentity(string Id, string Name, string Model, string SpecType, string HomeId, string Room, bool Online)
{
    public DeviceAdapter? Adapter { get; init; }
    public string? AdapterIssue { get; init; }
    public string SubclassId { get; init; } = "";
    public string CardSize { get; init; } = "";
    public string CardAppearance { get; init; } = "";
    public string CardCategory { get; init; } = "";
    public string CardIcon { get; init; } = "";
    public string RoomId { get; init; } = "";
    public bool SupportsEnergyHistory { get; init; }
    public string Summary => $"{Room} · {(Online ? "在线" : "离线或状态未知")} · {Model}";
    public override string ToString() => Name;
}
public record PropertyKey(string DeviceId, int Siid, int Piid);
public record EnumValue(JsonElement Value, string Description)
{
    public string DisplayDescription { get; init; } = "";
    public string DisplayLabel => !string.IsNullOrEmpty(DisplayDescription) ? DisplayDescription : Description ?? "";
}
public record PropertySchema(int Siid, int Piid, string Service, string Name, string Type,
    string Unit, bool Readable, bool Writable, double[] Range, List<EnumValue> Values)
{
    public string ServiceType { get; init; } = "";
    public string PropertyType { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string DisplayService { get; init; } = "";
}
public record DeviceSchema(List<PropertySchema> Properties)
{
    public List<ActionSchema> Actions { get; init; } = [];
}
public record ActionSchema(int Siid, int Aiid, string Name, string ServiceType, string ActionType, List<PropertySchema> Inputs, List<PropertySchema> Outputs)
{
    public string DisplayName { get; init; } = "";
}
public record AdapterAction(string Id, ActionSchema Action);
public record ActionRequest(string DeviceId, string AdapterId, string ActionId, List<object?> Arguments, string RequestId, bool Confirmed);
public record ActionResult(string Status, string Message, List<JsonElement> Outputs);
public record SpeakerActionRequest(string DeviceId, string HomeId, string Command, string RequestId, string? Text = null, bool Quiet = false);

public interface ISpeakerActionService
{
    Task<ActionResult> SendSpeakerActionAsync(SpeakerActionRequest request);
}
public interface IDeviceActionService
{
    bool IsSimulation => false;
    Task<ActionResult> InvokeActionAsync(ActionRequest request);
}
public record PropertyState(PropertyKey Key, JsonElement? Value, DateTimeOffset? ReadAt,
    bool IsValid, string? Error, bool IsWriting);
public interface IDeviceService
{
    Task<DeviceSchema> GetSchemaAsync(string deviceId);
    Task<JsonElement> ReadAsync(string deviceId);
    Task WriteAsync(PropertyKey key, object? value);
}
public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public record DeviceCardState(string DeviceId, DeviceSchema Schema, JsonElement Values, DateTimeOffset? ReadAt, string? Error)
{
    public DeviceAdapter? Adapter { get; init; }
}
public record AdapterBinding(string Id, PropertySchema Property, bool AllowWrite, string Capability);
public record AdapterCondition(string Binding, [property: System.Text.Json.Serialization.JsonPropertyName("equals")] JsonElement Value);
public record AdapterWidget(string Kind, string Label, string? Binding, List<AdapterWidget>? Children, AdapterCondition? When)
{
    public AdapterCondition? EnabledWhen { get; init; }
    public string? LabelKey { get; init; }
    public string Presentation { get; init; } = "auto";
    public string? ActiveBinding { get; init; }
    public List<AdapterOption>? Options { get; init; }
    public List<AdapterIndicatorState>? States { get; init; }
    public int MaxLength { get; init; } = 128;
    public string? Action { get; init; }
    public List<JsonElement>? Arguments { get; init; }
}
public record AdapterOption(JsonElement Value, string Label) { public string? LabelKey {get;init;} }
public record AdapterIndicatorState(JsonElement Value, string Label, string Tone) { public string? LabelKey {get;init;} }
public record DeviceAdapter(string Id, string Name, string Version, string Category, List<AdapterBinding> Bindings,
    List<AdapterWidget> Card, List<AdapterWidget> Drawer, List<AdapterWidget> Full, string IconFile)
{
    public List<AdapterWidget> CardExpanded { get; init; } = [];
    public bool CardExpandedDeclared { get; init; }
    public AdapterLocalization? Localization { get; init; }
    public List<AdapterAction> Actions { get; init; } = [];
}
public record AdapterPackage(string Id, string Name, string Version, List<string> Models, bool Enabled, bool CanRollback)
{
    public int FormatVersion {get;init;} = 1;
    public string SdkVersion {get;init;} = "";
    public string Author {get;init;} = "";
    public string Readme {get;init;} = "";
    public string IconFile {get;init;} = "";
}
public record HomePreferences(List<string> Pins, Dictionary<string,string> Sources);

public sealed record AdapterLocalization(string DefaultLocale, Dictionary<string, Dictionary<string,string>> Resources);
