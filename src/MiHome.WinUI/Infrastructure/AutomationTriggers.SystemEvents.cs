using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.Networking.Connectivity;

namespace MiHome.WinUI.Infrastructure;

internal sealed partial class AutomationTriggers
{
    internal const string UsbSelector = "(System.Devices.InterfaceClassGuid:=\"{A5DCBF10-6530-11D2-901F-00C04FB951ED}\" OR " +
        "System.Devices.InterfaceClassGuid:=\"{F18A0E88-C30C-11D0-8815-00A0C906BED8}\") AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
    private static readonly Guid PowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly Guid Battery = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    private static readonly Guid Display = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private readonly Dictionary<Guid, nint> powerNotifications = [];
    private readonly Dictionary<Guid, int> powerValues = [];
    private readonly Dictionary<Guid, (AutomationRule Rule, long Expires)> recovery = [];
    private DeviceWatcher? usbWatcher;
    private bool usbEnumerated, audioSubscribed, networkSubscribed, checkingCloud;
    private bool? internet;
    private string? audioOutput;
    private long nextCloudCheck;

    private void RegisterPowerNotifications()
    {
        foreach (var setting in new[] { PowerSource, Battery, Display })
        {
            var handle = RegisterPowerSettingNotification(host.Handle, setting, 0);
            if (handle != 0) powerNotifications[setting] = handle;
        }
    }

    private void ConfigureSystemEvents(List<AutomationRule> enabled, List<string> failures)
    {
        void Warn(string kind) => failures.Add(UiText.Get(AutomationRule.TriggerLabel(kind)));
        if (enabled.Any(rule => rule.Trigger == "ac-power") && !powerNotifications.ContainsKey(PowerSource)) Warn("ac-power");
        if (enabled.Any(rule => rule.Trigger == "battery-low") && !powerNotifications.ContainsKey(Battery)) Warn("battery-low");
        if (enabled.Any(rule => rule.Trigger == "display-off") && !powerNotifications.ContainsKey(Display)) Warn("display-off");

        try
        {
            if (enabled.Any(rule => rule.Trigger == "usb-attach"))
            {
                if (usbWatcher?.Status == DeviceWatcherStatus.Aborted) StopUsbWatcher();
                if (usbWatcher is null)
                {
                    var watcher = DeviceInformation.CreateWatcher(UsbSelector, [], DeviceInformationKind.DeviceInterface);
                    watcher.Added += OnUsbAdded;
                    watcher.Updated += OnUsbUpdated;
                    watcher.Removed += OnUsbRemoved;
                    watcher.EnumerationCompleted += OnUsbEnumerated;
                    watcher.Start();
                    usbWatcher = watcher;
                }
            }
            else StopUsbWatcher();
        }
        catch (Exception) { Warn("usb-attach"); }

        try
        {
            if (enabled.Any(rule => rule.Trigger == "audio-output"))
            {
                if (!audioSubscribed)
                {
                    audioOutput = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
                    MediaDevice.DefaultAudioRenderDeviceChanged += OnAudioOutputChanged;
                    audioSubscribed = true;
                }
            }
            else StopAudio();
        }
        catch (Exception) { Warn("audio-output"); }

        try
        {
            if (enabled.Any(rule => rule.Trigger is "network-online" or "wake"))
            {
                if (!networkSubscribed)
                {
                    internet = ReadInternet();
                    NetworkInformation.NetworkStatusChanged += OnNetworkChanged;
                    networkSubscribed = true;
                }
            }
            else StopNetwork();
        }
        catch (Exception)
        {
            foreach (var kind in enabled.Select(rule => rule.Trigger).Where(kind => kind is "network-online" or "wake").Distinct()) Warn(kind);
        }

        foreach (var (id, pending) in recovery.ToArray())
            if (!enabled.Any(rule => ReferenceEquals(rule, pending.Rule))) recovery.Remove(id);
        UpdateSampleTimer();
    }

    private void ApplyPowerSetting(PowerSetting setting)
    {
        if (disposed || (setting.Id == Battery && setting.Value is < 0 or > 100)) return;
        var hasBaseline = powerValues.TryGetValue(setting.Id, out var previous);
        powerValues[setting.Id] = setting.Value;
        if (suspended || !hasBaseline || previous == setting.Value) return;
        if (setting.Id == PowerSource && setting.Value == 0) _ = engine.TriggerAsync("ac-power");
        if (setting.Id == Display && setting.Value == 0) _ = engine.TriggerAsync("display-off");
        if (setting.Id == Battery)
            _ = engine.TriggerAsync(rule => rule.Trigger == "battery-low" &&
                AutomationRule.TryThreshold(rule.Argument, 99, out var threshold) &&
                AutomationRule.CrossedThreshold(previous, setting.Value, threshold, rising: false));
    }

    private readonly HashSet<string> connectedUsb = new(StringComparer.OrdinalIgnoreCase);

    private void OnUsbEnumerated(DeviceWatcher sender, object args) => dispatcher.TryEnqueue(() =>
    {
        if (!disposed && sender == usbWatcher) usbEnumerated = true;
    });

    private void OnUsbAdded(DeviceWatcher sender, DeviceInformation device)
    {
        var id = device.Id;
        dispatcher.TryEnqueue(() =>
        {
            if (disposed || sender != usbWatcher || !connectedUsb.Add(id) || !usbEnumerated || suspended) return;
            _ = engine.TriggerAsync("usb-attach", id);
        });
    }

    private void OnUsbRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        var id = device.Id;
        dispatcher.TryEnqueue(() => { if (!disposed && sender == usbWatcher) connectedUsb.Remove(id); });
    }

    // DeviceWatcher needs all three handlers to continue reporting changes after enumeration.
    private static void OnUsbUpdated(DeviceWatcher sender, DeviceInformationUpdate device) { }

    private void OnAudioOutputChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        if (args.Role != AudioDeviceRole.Default) return;
        var id = args.Id;
        dispatcher.TryEnqueue(() =>
        {
            if (disposed || !audioSubscribed || string.Equals(id, audioOutput, StringComparison.OrdinalIgnoreCase)) return;
            audioOutput = id;
            if (!suspended && !string.IsNullOrEmpty(id)) _ = engine.TriggerAsync("audio-output");
        });
    }

    private void OnNetworkChanged(object sender) => dispatcher.TryEnqueue(() =>
    {
        if (disposed || !networkSubscribed) return;
        var previous = internet;
        internet = ReadInternet();
        if (!suspended && previous == false && internet == true) QueueRecovery("network-online");
    });

    private static bool? ReadInternet()
    {
        try { return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess; }
        catch (Exception) { return null; }
    }

    private void QueueRecovery(string kind)
    {
        if (engine.Paused) return;
        var expires = Environment.TickCount64 + 60_000;
        foreach (var rule in engine.Rules.Where(rule => rule.Enabled && rule.Trigger == kind)) recovery[rule.Id] = (rule, expires);
        UpdateSampleTimer();
    }

    private async Task DispatchRecoveryAsync()
    {
        var now = Environment.TickCount64;
        foreach (var (id, pending) in recovery.ToArray()) if (pending.Expires <= now) recovery.Remove(id);
        UpdateSampleTimer();
        if (recovery.Count == 0 || checkingCloud || now < nextCloudCheck || !engine.CanCheckConnection || ReadInternet() != true) return;
        var probe = recovery.ToArray();
        checkingCloud = true;
        nextCloudCheck = now + 5_000;
        try
        {
            if (!await engine.CheckConnectionAsync() || disposed || suspended || !engine.CanCheckConnection || ReadInternet() != true) return;
            var ready = new HashSet<Guid>();
            foreach (var (id, pending) in probe)
            {
                if (pending.Expires <= Environment.TickCount64 || !recovery.TryGetValue(id, out var current) || current != pending) continue;
                recovery.Remove(id);
                ready.Add(id);
            }
            if (ready.Count != 0) _ = engine.TriggerAsync(rule => ready.Contains(rule.Id));
        }
        finally
        {
            checkingCloud = false;
            if (!disposed) UpdateSampleTimer();
        }
    }

    private void StopUsbWatcher()
    {
        var watcher = usbWatcher;
        usbWatcher = null;
        usbEnumerated = false;
        connectedUsb.Clear();
        if (watcher is null) return;
        watcher.Added -= OnUsbAdded;
        watcher.Updated -= OnUsbUpdated;
        watcher.Removed -= OnUsbRemoved;
        watcher.EnumerationCompleted -= OnUsbEnumerated;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
    }

    private void StopAudio()
    {
        if (audioSubscribed) MediaDevice.DefaultAudioRenderDeviceChanged -= OnAudioOutputChanged;
        audioSubscribed = false;
        audioOutput = null;
    }

    private void StopNetwork()
    {
        if (networkSubscribed) NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        networkSubscribed = false;
        internet = null;
    }

    private void DisposeSystemEvents()
    {
        recovery.Clear();
        StopUsbWatcher();
        StopAudio();
        StopNetwork();
        foreach (var handle in powerNotifications.Values) UnregisterPowerSettingNotification(handle);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PowerSetting { public Guid Id; public int Size, Value; }

    [DllImport("user32.dll")] private static extern nint RegisterPowerSettingNotification(nint recipient, in Guid setting, uint flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(nint handle);
}
