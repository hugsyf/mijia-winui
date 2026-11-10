using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;

internal static class AutomationChecks
{
    public static async Task RunAsync()
    {
        var rule = new AutomationRule(Guid.NewGuid(), "Game", "test-home", true, "hotkey", "G", "18:00", "06:00",
            [new("wait", "", Seconds: 2)]);
        Require(rule.IsValid(), "A valid hotkey rule is accepted");
        Require(rule.MatchesTime(new(18, 0)) && rule.MatchesTime(new(0, 0)) && rule.MatchesTime(new(5, 59)), "Midnight window includes the evening and morning");
        Require(!rule.MatchesTime(new(6, 0)) && !rule.MatchesTime(new(17, 59)), "Window end is exclusive");
        Require(!(rule with { Until = "18:00" }).IsValid() && !(rule with { Argument = "F12" }).IsValid(), "Invalid intervals and keys cannot be enabled");
        Require(!(rule with { Steps = [new("shell", "")]}).IsValid(), "Unknown step kinds are rejected");

        var weekday = rule with { Trigger = "schedule", Argument = "18:30|62" };
        Require(weekday.IsValid() && weekday.IsScheduledAt(new(2026, 11, 9, 18, 30, 59)) &&
            !weekday.IsScheduledAt(new(2026, 11, 8, 18, 30, 0)) && !weekday.IsScheduledAt(new(2026, 11, 9, 18, 31, 0)),
            "The local schedule matches the selected weekday and minute");
        Require(!(weekday with { Argument = "18:30|0" }).IsValid() && !(weekday with { Argument = "24:00|127" }).IsValid(),
            "A schedule needs a valid time and at least one weekday");
        Require((rule with { Trigger = "process-start", Argument = @"C:\Games\game.exe" }).IsValid() &&
            !(rule with { Trigger = "process-exit", Argument = "game.exe" }).IsValid(), "Process rules require an absolute executable path");
        Require((rule with { Trigger = "idle", Argument = "15" }).IsValid() &&
            (rule with { Trigger = "active", Argument = "15" }).IsValid() && !(rule with { Trigger = "idle", Argument = "241" }).IsValid(),
            "Idle and input rules share the supported duration range");
        Require((rule with { Trigger = "battery-low", Argument = "20" }).IsValid() &&
            !(rule with { Trigger = "battery-low", Argument = "100" }).IsValid() &&
            AutomationRule.CrossedThreshold(21, 20, 20, rising: false) && !AutomationRule.CrossedThreshold(20, 19, 20, rising: false) &&
            AutomationRule.CrossedThreshold(14.9, 15, 15, rising: true), "Thresholds fire once at the boundary in the selected direction");
        Require((rule with { Trigger = "usb-attach", Argument = "device-interface-id", TargetLabel = "USB Hub" }).IsValid() &&
            !(rule with { Trigger = "usb-attach", Argument = "device-interface-id" }).IsValid(), "A USB rule stores a device ID and label");
        var existing = JsonSerializer.SerializeToNode(rule)!.AsObject();
        existing.Remove(nameof(AutomationRule.TargetLabel));
        Require(existing.Deserialize<AutomationRule>() is { } loaded && loaded.IsValid(), "Saved rules without a USB label still load");

        var property = new PropertySchema(2, 1, "Switch", "Power", "bool", "", true, true, [], [])
        {
            ServiceType = "urn:miot-spec-v2:service:switch:0000780C",
            PropertyType = "urn:miot-spec-v2:property:on:00000006"
        };
        var device = new DeviceIdentity("plug", "Plug", "cuco.plug.v3", "fixture", "test-home", "", true);
        var schema = new DeviceSchema([property]);
        var service = new StateService(schema);
        var control = new DeviceControlSession(device, schema, service);
        Require(await control.WriteAsync(property, false) == DeviceWriteResult.Confirmed && service.Writes == 0, "Already satisfied steps complete without a write");
        Require(await control.WriteAsync(property, true) == DeviceWriteResult.Unknown && service.Writes == 1, "Unconfirmed writes report unknown and are not retried");
        service.On = true;
        Require(await control.WriteAsync(property, true) == DeviceWriteResult.Confirmed && service.Writes == 1, "Readback verifies an already-applied target");
        Console.WriteLine("PASS: 15 automation checks (schedule, trigger validation, time window and device write outcomes)");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class StateService(DeviceSchema schema) : IDeviceService
    {
        public bool On { get; set; }
        public int Writes { get; private set; }
        public Task<DeviceSchema> GetSchemaAsync(string deviceId) => Task.FromResult(schema);
        public Task<JsonElement> ReadAsync(string deviceId) => Task.FromResult(
            JsonSerializer.SerializeToElement(new[] { new { siid = 2, piid = 1, code = 0, value = On } }));
        public Task WriteAsync(PropertyKey key, object? value) { Writes++; return Task.CompletedTask; }
    }
}
