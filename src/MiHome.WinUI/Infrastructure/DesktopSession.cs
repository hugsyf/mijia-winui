using System.Text.Json;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;

namespace MiHome.WinUI.Infrastructure;

// One backend and device store are shared by every desktop surface.
internal sealed class DesktopSession : IDisposable
{
    public BackendClient Backend { get; } = new();
    public DeviceBrowserViewModel Browser { get; } = new();
    public bool Authenticated { get; set; }
    public bool MainBusy { get; set; }
    public bool Busy { get; private set; }
    public Home? Home { get; private set; }
    public List<Home> Homes { get; private set; } = [];
    public List<string> Pins { get; private set; } = [];
    public event Action? Changed;
    public event Action? AuthenticationLost;
    public event Action<DeviceTile>? OpenDeviceRequested;
    public event Action<string>? HomeRequested;
    public event Action? Updated;
    private int contextVersion;
    private int pinRevision;
    private int operationVersion;
    public List<DeviceTile> Available => Home is null ? [] : Browser.HomeTiles.Where(t => t.Device.HomeId == Home.Id).ToList();
    public List<DeviceTile> Pinned => Pins.Select(id => Available.FirstOrDefault(t => t.Device.Id == id)).OfType<DeviceTile>().ToList();
    public void Notify() => Changed?.Invoke();
    public void OpenDevice(DeviceTile tile) => OpenDeviceRequested?.Invoke(tile);
    public void SelectHome(string id) => HomeRequested?.Invoke(id);
    public void Publish(DeviceCardState state)
    {
        if (!Authenticated || !Available.Any(t => t.Device.Id == state.DeviceId)) return;
        Browser.UpdateCardState(state);
        Updated?.Invoke();
        Notify();
    }
    public Action<DeviceCardState> CapturePublisher()
    {
        var generation = contextVersion;
        return state => { if (generation == contextVersion) Publish(state); };
    }
    public IDeviceService QuickService(string deviceId) => new ScopedService(this, deviceId, contextVersion,
        Available.First(t => t.Device.Id == deviceId).Device.Adapter);
    public async Task SetHomeAsync(Home home, List<Home> homes)
    {
        Homes = homes;
        if (Home?.Id == home.Id) { Home = home; return; }
        var generation = ++contextVersion;
        Home = home; Pins = []; Notify();
        var result = await Backend.CallAsync("GetTrayPreferences", new { homeId = home.Id });
        if (generation != contextVersion) return;
        Pins = result.GetProperty("pins").Deserialize<List<string>>() ?? [];
        Notify();
    }
    public void Clear()
    {
        contextVersion++;
        operationVersion++; Busy = false;
        Authenticated = false; Home = null; Homes = []; Pins = [];
        Browser.SetDevices([]); Notify();
    }
    public async Task SavePinsAsync(IEnumerable<string> ids)
    {
        if (Home is not { } home || !Authenticated) return;
        var generation = contextVersion;
        var revision = ++pinRevision;
        var pins = ids.Distinct().ToList();
        if (pins.Count > 12 || pins.Any(id => !Available.Any(t => t.Device.Id == id)))
            throw new InvalidOperationException(UiText.Get("TrayPinLimit"));
        await Backend.CallAsync("SetTrayPreferences", new { homeId = home.Id, pins });
        if (generation == contextVersion && revision == pinRevision) { Pins = pins; Notify(); }
    }
    public async Task RefreshAsync()
    {
        if (!Authenticated || Home is null || Busy || MainBusy || !Backend.Running) return;
        var generation = contextVersion;
        var operation = ++operationVersion;
        var ids = Pinned.Where(t => t.Device.Online).Select(t => t.Device.Id).ToList();
        if (ids.Count == 0) return;
        Busy = true; Notify();
        try
        {
            var states = (await Backend.CallAsync("ReadCardStates", new { deviceIds = ids })).Deserialize<List<DeviceCardState>>(BackendClient.Json) ?? [];
            if (generation != contextVersion) return;
            foreach (var state in states) Browser.UpdateCardState(state);
            Updated?.Invoke();
        }
        catch (BridgeException error) when (error.Code == "auth") { if (generation == contextVersion) AuthenticationLost?.Invoke(); }
        finally { if (operation == operationVersion) Busy = false; Notify(); }
    }
    public async Task<string?> WriteAsync(DeviceTile tile, PropertySchema property, object value)
    {
        if (Busy || MainBusy || !Authenticated || !Available.Any(t => t.Device.Id == tile.Device.Id)) return UiText.Get("TrayBusy");
        var generation = contextVersion;
        var operation = ++operationVersion;
        Busy = true; Notify();
        try
        {
            var control = new DeviceControlSession(tile.Device, tile.State!.Schema, QuickService(tile.Device.Id));
            control.StateRead += state => { if (generation == contextVersion) Publish(state); };
            await control.WriteAsync(property, value);
            return control.Feedback;
        }
        catch (BridgeException error) when (error.Code == "auth") { if (generation == contextVersion) AuthenticationLost?.Invoke(); return error.Message; }
        finally { if (operation == operationVersion) Busy = false; Notify(); }
    }
    public void Dispose() => Backend.Dispose();
    private sealed class ScopedService(DesktopSession owner, string deviceId, int generation, DeviceAdapter? adapter) : IDeviceService, IDeviceActionService
    {
        private void Check(string id, bool write = false)
        {
            if (!owner.Authenticated || generation != owner.contextVersion || id != deviceId || !owner.Available.Any(t => t.Device.Id == id && t.Device.Adapter == adapter))
                throw new BridgeException("context", UiText.Get("TrayContextChanged"));
            if (write && owner.MainBusy) throw new BridgeException("busy", UiText.Get("TrayBusy"));
        }
        private async Task<T> Execute<T>(Func<Task<T>> action)
        {
            try { return await action(); }
            catch (BridgeException error) when (error.Code == "auth")
            { if (generation == owner.contextVersion) owner.AuthenticationLost?.Invoke(); throw; }
        }
        public async Task<DeviceSchema> GetSchemaAsync(string id)
        { Check(id); var result = await Execute(() => owner.Backend.GetSchemaAsync(id)); Check(id); return result; }
        public async Task<JsonElement> ReadAsync(string id)
        { Check(id); var result = await Execute(() => owner.Backend.ReadAsync(id)); Check(id); return result; }
        public async Task WriteAsync(PropertyKey key, object? value)
        { Check(key.DeviceId, true); await Execute(async () => { await owner.Backend.WriteAsync(key, value); return true; }); }
        public Task<ActionResult> InvokeActionAsync(ActionRequest request)
        { Check(request.DeviceId, true); return Execute(() => owner.Backend.InvokeActionAsync(request)); }
    }
}
