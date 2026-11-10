using System.Text.Json;
using System.Text.Json.Serialization;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;

namespace MiHome.WinUI.Infrastructure;

public sealed record AutomationRunEntry(DateTimeOffset Time, string Name, string Status, int Step)
{
    [JsonIgnore] public string Detail => $"{Time:g} · " + (Step > 0 ? UiText.Format("AutoStepNumber", Step) + " · " : "") + UiText.Get(Status);
}

internal sealed class AutomationEngine(DesktopSession session, string directory)
{
    private sealed record Document(int Version, bool Paused, List<AutomationRule> Rules, List<AutomationRunEntry> History);
    private Document document = new(1, false, [], []);
    private CancellationTokenSource? running;
    private AutomationRule? activeRule;
    private string? runId;
    private string cancelReason = "AutoCancelled";
    private bool storageInvalid;
    private bool manualSceneRunning;
    public string? Account { get; private set; }
    public bool Paused => document.Paused;
    public bool Running => running is not null;
    public bool StorageInvalid => storageInvalid;
    public IReadOnlyList<AutomationRule> Rules => document.Rules;
    public IReadOnlyList<AutomationRunEntry> History => document.History;
    public event Action? Changed;
    public event Action? RulesChanged;
    private string FilePath(string account) => Path.Combine(directory, "automation", account + ".json");

    public void Bind(string? account)
    {
        if (Account == account) return;
        Cancel();
        Account = account;
        document = new(1, false, [], []);
        storageInvalid = false;
        if (account is not null && File.Exists(FilePath(account)))
        {
            try
            {
                var path = FilePath(account);
                if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException();
                var loaded = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), BackendClient.Json);
                if (loaded is not { Version: 1, Rules.Count: <= 50, History.Count: <= 50 } ||
                    loaded.Rules.Any(rule => rule is null || !rule.IsValid()) ||
                    loaded.Rules.Select(rule => rule.Id).Distinct().Count() != loaded.Rules.Count ||
                    loaded.History.Any(entry => entry is not { Name.Length: <= 80, Status.Length: > 0 and <= 60 }))
                    throw new InvalidDataException();
                document = loaded;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { storageInvalid = true; }
        }
        RulesChanged?.Invoke();
        Changed?.Invoke();
    }

    private void Persist(Document next)
    {
        if (Account is not { } account || storageInvalid) throw new InvalidOperationException(UiText.Get("AutoStorage"));
        var path = FilePath(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(next, BackendClient.Json));
        File.Move(temporary, path, true);
        document = next;
    }

    public void Save(AutomationRule rule)
    {
        if (!rule.IsValid() || !session.Homes.Any(home => home.Id == rule.HomeId))
            throw new InvalidOperationException(UiText.Get("AutoInvalid"));
        var rules = document.Rules.ToList();
        var index = rules.FindIndex(item => item.Id == rule.Id);
        if (index >= 0) rules[index] = rule;
        else if (rules.Count < 50) rules.Add(rule);
        else throw new InvalidOperationException(UiText.Get("AutoInvalid"));
        Persist(document with { Rules = rules });
        if (activeRule?.Id == rule.Id) Cancel();
        RulesChanged?.Invoke();
        Changed?.Invoke();
    }

    public void Delete(Guid id)
    {
        Persist(document with { Rules = document.Rules.Where(rule => rule.Id != id).ToList() });
        if (activeRule?.Id == id) Cancel();
        RulesChanged?.Invoke();
        Changed?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        if (Account is null || storageInvalid) throw new InvalidOperationException(UiText.Get("AutoStorage"));
        if (paused)
        {
            document = document with { Paused = true };
            Cancel();
        }
        try { Persist(document with { Paused = paused }); }
        finally
        {
            RulesChanged?.Invoke();
            Changed?.Invoke();
        }
    }

    public async Task<List<CloudScene>> ScenesAsync(string homeId)
    {
        var result = await session.Backend.CallAsync("ListScenes", new { homeId });
        return result.Deserialize<List<CloudScene>>(BackendClient.Json) ?? [];
    }

    public async Task<bool> RunManualSceneAsync(CloudScene scene)
    {
        var account = Account ?? throw new InvalidOperationException(UiText.Get("AutoStorage"));
        if (manualSceneRunning) return false;
        manualSceneRunning = true;
        Cancel("AutoManualConflict");
        try
        {
            var result = await session.Backend.CallAsync("RunScene", new { homeId = scene.HomeId, sceneId = scene.Id,
                automation = Scope(account, scene.HomeId, "", Guid.NewGuid().ToString("N")) });
            return result.GetProperty("accepted").GetBoolean();
        }
        finally { manualSceneRunning = false; }
    }

    public void ManualWrite(string id)
    {
        if (activeRule?.Steps.Any(step => step.Kind == "scene" || step.Kind == "write" &&
            PhysicalId(step.TargetId!) == PhysicalId(id)) == true) Cancel("AutoManualConflict");
    }

    private static string PhysicalId(string id)
    {
        var suffix = id.LastIndexOf(".s", StringComparison.Ordinal);
        return suffix >= 0 && int.TryParse(id.AsSpan(suffix + 2), out _) ? id[..suffix] : id;
    }

    public void Cancel(string reason = "AutoCancelled")
    {
        if (running is null || running.IsCancellationRequested) return;
        cancelReason = reason;
        running.Cancel();
        if (session.Backend.Running && runId is { } id) _ = CancelWorkerAsync(id);
    }

    private async Task CancelWorkerAsync(string id)
    {
        try { await session.Backend.CallAsync("CancelAutomationRun", new { runId = id }); }
        catch { /* The worker may have already stopped. */ }
    }

    public Task TriggerAsync(string trigger, string argument = "") => TriggerAsync(rule => rule.Trigger == trigger &&
        string.Equals(rule.Argument, argument, StringComparison.OrdinalIgnoreCase));

    public async Task TriggerAsync(Func<AutomationRule, bool> matchesEvent)
    {
        if (Paused || Account is null || !session.Authenticated || !session.Backend.Running) return;
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var matches = Rules.Where(rule => rule.Enabled && matchesEvent(rule) && rule.MatchesTime(now)).ToList();
        if (matches.Count == 0) return;
        var first = matches[0];
        foreach (var rule in matches.Skip(1)) Record(rule.Name, "AutoSkipped", 0);
        if (Running || manualSceneRunning || session.MainBusy || session.Busy) { Record(first.Name, "AutoSkipped", 0); return; }
        await RunAsync(first);
    }

    public bool CanCheckConnection => Account is not null && session.Authenticated && session.Backend.Running &&
        !Paused && !Running && !manualSceneRunning && !session.MainBusy && !session.Busy;

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var result = await session.Backend.CallAsync("GetSessionStatus");
            return result.GetProperty("authenticated").GetBoolean();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task RunAsync(AutomationRule rule)
    {
        var account = Account!;
        var id = Guid.NewGuid().ToString("N");
        using var cancellation = new CancellationTokenSource();
        running = cancellation;
        activeRule = rule;
        runId = id;
        var token = cancellation.Token;
        var index = 0;
        var status = "AutoCompleted";
        List<DeviceIdentity>? devices = null;
        Changed?.Invoke();
        try
        {
            if (!session.Homes.Any(home => home.Id == rule.HomeId)) throw new InvalidOperationException();
            foreach (var step in rule.Steps)
            {
                index++;
                token.ThrowIfCancellationRequested();
                if (Account != account || !session.Authenticated || !session.Backend.Running) throw new OperationCanceledException();
                if (step.Kind == "wait")
                {
                    await Task.Delay(TimeSpan.FromSeconds(step.Seconds), token);
                    continue;
                }
                if (step.Kind == "scene")
                {
                    var result = await session.Backend.CallAsync("RunScene", new { homeId = rule.HomeId, sceneId = step.TargetId,
                        automation = Scope(account, rule.HomeId, "", id) }, token);
                    if (!result.GetProperty("accepted").GetBoolean()) { status = "AutoFailed"; break; }
                    Record(rule.Name, "AutoAccepted", index);
                    continue;
                }

                devices ??= (await session.Backend.CallAsync("ListDevices", new { homeId = rule.HomeId }, token))
                    .Deserialize<List<DeviceIdentity>>(BackendClient.Json) ?? [];
                var device = devices.FirstOrDefault(item => item.Id == step.TargetId && item.SpecType == step.SpecType);
                if (device is null || !device.Online) { status = "AutoRepair"; break; }
                var schema = await session.Backend.GetSchemaAsync(device.Id);
                var property = schema.Properties.FirstOrDefault(item => item.Siid == step.Siid && item.Piid == step.Piid);
                if (property is null || !AutomationRule.CanOffer(device, schema, property)) { status = "AutoRepair"; break; }
                var control = new DeviceControlSession(device, schema, new RuleDeviceService(session.Backend, account, rule.HomeId, device.SpecType, id, token));
                var publish = session.CapturePublisher();
                control.StateRead += state => { if (account == Account && session.Home?.Id == rule.HomeId) publish(state); };
                var outcome = await control.WriteAsync(property, step.Value!.Value);
                if (outcome == DeviceWriteResult.Unknown) { status = "AutoUnknown"; break; }
                token.ThrowIfCancellationRequested();
                if (outcome != DeviceWriteResult.Confirmed) { status = "AutoFailed"; break; }
                Record(rule.Name, "AutoConfirmed", index);
            }
        }
        catch (OperationCanceledException) { status = cancelReason; }
        catch (BridgeException error) when (error.Code is "actionUnknown" or "network") { status = "AutoUnknown"; }
        catch (BridgeException error) when (error.Code == "auth") { status = "AutoLoginRequired"; }
        catch { status = token.IsCancellationRequested ? cancelReason : "AutoFailed"; }
        finally
        {
            if (token.IsCancellationRequested && status == "AutoCompleted") status = cancelReason;
            running = null;
            activeRule = null;
            runId = null;
            if (Account == account) Record(rule.Name, status, status == "AutoCompleted" ? 0 : index);
            if (status == "AutoLoginRequired" && Account == account) session.ReportAuthenticationLost();
            Changed?.Invoke();
        }
    }

    private void Record(string name, string status, int step)
    {
        if (Account is null) return;
        var next = document with { History = [new(DateTimeOffset.Now, name, status, step), .. document.History.Take(49)] };
        try { Persist(next); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { document = next; }
        Changed?.Invoke();
    }

    private static object Scope(string account, string homeId, string specType, string runId) =>
        new { account, homeId, specType, runId, expiresAt = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() };

    private sealed class RuleDeviceService(BackendClient backend, string account, string homeId, string specType,
        string runId, CancellationToken token) : IDeviceService
    {
        public Task<DeviceSchema> GetSchemaAsync(string deviceId) => backend.GetSchemaAsync(deviceId);
        public Task<JsonElement> ReadAsync(string deviceId) => backend.ReadAsync(deviceId);
        public async Task WriteAsync(PropertyKey key, object? value) => await backend.CallAsync("WriteProperty", new
        {
            deviceId = key.DeviceId, key.Siid, key.Piid, value, automation = Scope(account, homeId, specType, runId)
        }, token);
    }
}
