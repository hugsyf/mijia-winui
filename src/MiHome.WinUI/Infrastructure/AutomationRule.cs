using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;

namespace MiHome.WinUI.Infrastructure;

public sealed record CloudScene(string Id, string Name, string HomeId);

public sealed record AutomationStep(string Kind, string Label, string? TargetId = null, string? SpecType = null,
    int Siid = 0, int Piid = 0, JsonElement? Value = null, int Seconds = 0)
{
    [JsonIgnore] public string Display => Kind == "wait" ? UiText.Format("AutoWaitLabel", Seconds) : Label;
}

public sealed record AutomationRule(Guid Id, string Name, string HomeId, bool Enabled, string Trigger,
    string Argument, string From, string Until, List<AutomationStep> Steps, string? TargetLabel = null)
{
    [JsonIgnore] public string Display => $"{Name} · {UiText.Get(Enabled ? "Enabled" : "Disabled")}";

    public bool MatchesTime(TimeOnly now)
    {
        if (From.Length == 0 && Until.Length == 0) return true;
        if (!ParseTime(From, out var from) || !ParseTime(Until, out var until) || from == until) return false;
        return from < until ? now >= from && now < until : now >= from || now < until;
    }

    private static bool ParseTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public bool IsValid() => Id != Guid.Empty && Name is { Length: > 0 and <= 80 } &&
        HomeId is { Length: > 0 and <= 512 } && Argument is not null && TargetLabel is null or { Length: <= 256 } &&
        (Trigger switch
        {
            "foreground" or "foreground-exit" or "process-start" or "process-exit" =>
                Argument.Length <= 1024 && Path.IsPathFullyQualified(Argument) &&
                string.Equals(Path.GetExtension(Argument), ".exe", StringComparison.OrdinalIgnoreCase),
            "hotkey" => Hotkey(Argument) != 0,
            "idle" or "active" => TryThreshold(Argument, 240, out _),
            "battery-low" => TryThreshold(Argument, 99, out _),
            "schedule" => TrySchedule(Argument, out _, out _),
            "usb-attach" => Argument.Length is > 0 and <= 1024 && TargetLabel is { Length: > 0 },
            "lock" or "unlock" or "wake" or "ac-power" or "display-off" or "audio-output" or "network-online" => Argument.Length == 0,
            _ => false
        }) && (From == "" && Until == "" || ParseTime(From, out var from) && ParseTime(Until, out var until) && from != until) &&
        Steps is { Count: > 0 and <= 20 } && Steps.All(ValidStep);

    private static bool ValidStep(AutomationStep step) => step is not null && step.Label is { Length: <= 400 } &&
        (step.Kind switch
        {
            "wait" => step.Seconds is >= 1 and <= 300,
            "scene" => step.TargetId is { Length: > 0 and <= 512 },
            "write" => step.TargetId is { Length: > 0 and <= 512 } && step.SpecType is { Length: > 0 and <= 512 } &&
                       step.Siid > 0 && step.Piid > 0 && step.Value is { ValueKind: JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number },
            _ => false
        });

    public static uint Hotkey(string value) => value is { Length: 1 } && value[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? value[0] : 0u;

    internal static bool UsesProgram(string? trigger) => trigger is "foreground" or "foreground-exit" or "process-start" or "process-exit";

    internal static bool TryThreshold(string argument, int maximum, out int value) =>
        int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= maximum;

    internal static bool TrySchedule(string argument, out TimeOnly time, out int days)
    {
        time = default;
        days = 0;
        return argument.Length is >= 7 and <= 9 && argument[5] == '|' &&
            ParseTime(argument[..5], out time) && TryThreshold(argument[6..], 127, out days);
    }

    internal bool IsScheduledAt(DateTime local) => TrySchedule(Argument, out var time, out var days) &&
        time.Hour == local.Hour && time.Minute == local.Minute && (days & (1 << (int)local.DayOfWeek)) != 0;

    internal static bool CrossedThreshold(double before, double after, double threshold, bool rising) =>
        rising ? before < threshold && after >= threshold : before > threshold && after <= threshold;

    internal static string TriggerLabel(string kind) => kind switch
    {
        "foreground" => "AutoForeground",
        "foreground-exit" => "AutoForegroundExit",
        "process-start" => "AutoProcessStart",
        "process-exit" => "AutoProcessExit",
        "lock" => "AutoLock",
        "unlock" => "AutoUnlock",
        "hotkey" => "AutoShortcut",
        "idle" => "AutoIdle",
        "active" => "AutoActive",
        "schedule" => "AutoSchedule",
        "wake" => "AutoWake",
        "ac-power" => "AutoAcPower",
        "battery-low" => "AutoBatteryLow",
        "display-off" => "AutoDisplayOff",
        "usb-attach" => "AutoUsbAttach",
        "audio-output" => "AutoAudioOutput",
        "network-online" => "AutoNetworkOnline",
        _ => "AutoTriggersUnavailable"
    };

    public static bool CanOffer(DeviceIdentity device, DeviceSchema schema, PropertySchema property)
    {
        if (!property.Readable || !property.Writable) return false;
        if (device.Adapter is { } adapter && !adapter.Bindings.Any(binding => binding.AllowWrite &&
            binding.Property.Siid == property.Siid && binding.Property.Piid == property.Piid)) return false;
        var service = DeviceCardPresentation.Semantic(property.ServiceType);
        var name = DeviceCardPresentation.Semantic(property.PropertyType);
        if (service == "light") return name is "on" or "brightness" or "color-temperature" or "color";
        return service == "switch" && name == "on" && DeviceExperience.PowerProperties(device, schema).Contains(property);
    }
}
