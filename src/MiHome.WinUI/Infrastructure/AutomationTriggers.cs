using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace MiHome.WinUI.Infrastructure;

internal sealed partial class AutomationTriggers : IDisposable
{
    private const uint ForegroundEvent = 3;
    private const uint HotkeyModifiers = 0x4003; // Ctrl + Alt, without key repeat.
    private readonly TrayIconService host;
    private readonly AutomationEngine engine;
    private readonly DispatcherQueue dispatcher;
    private readonly ForegroundCallback callback;
    private readonly HashSet<uint> hotkeys = [];
    private readonly bool sessionNotifications;
    private nint hook;
    private readonly ForegroundProgramState foreground = new();
    private bool watchForeground;
    private string? observedAccount;
    private bool suspended;
    private bool disposed;
    public string Unavailable { get; private set; } = "";

    public AutomationTriggers(TrayIconService host, AutomationEngine engine, DispatcherQueue dispatcher)
    {
        this.host = host;
        this.engine = engine;
        this.dispatcher = dispatcher;
        callback = OnForeground;
        sampleTimer = dispatcher.CreateTimer();
        sampleTimer.Interval = TimeSpan.FromMilliseconds(250);
        sampleTimer.Tick += (_, _) => SamplePcState();
        host.WindowMessage += OnWindowMessage;
        sessionNotifications = WTSRegisterSessionNotification(host.Handle, 0);
        RegisterPowerNotifications();
        engine.RulesChanged += Reconfigure;
        Reconfigure();
    }

    private void Reconfigure()
    {
        if (observedAccount != engine.Account)
        {
            observedAccount = engine.Account;
            ResetPcSampling();
            lastScheduleDate.Clear();
            recovery.Clear();
            foreground.Reset(ForegroundPath());
        }
        var enabled = engine.Rules.Where(rule => rule.Enabled && !engine.Paused).ToList();
        var keys = enabled.Where(rule => rule.Trigger == "hotkey").Select(rule => AutomationRule.Hotkey(rule.Argument)).ToHashSet();
        foreach (var key in hotkeys.Except(keys).ToArray())
        {
            UnregisterHotKey(host.Handle, (int)key);
            hotkeys.Remove(key);
        }
        var failures = new List<string>();
        foreach (var key in keys.Except(hotkeys).ToArray())
        {
            if (RegisterHotKey(host.Handle, (int)key, HotkeyModifiers, key)) hotkeys.Add(key);
            else failures.Add("Ctrl+Alt+" + (char)key);
        }
        if (enabled.Any(rule => rule.Trigger is "foreground" or "foreground-exit"))
        {
            if (!watchForeground) foreground.Reset(ForegroundPath());
            watchForeground = true;
            if (hook == 0)
            {
                hook = SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, callback, 0, 0, 0);
            }
        }
        else StopForeground();
        if (!sessionNotifications && enabled.Any(rule => rule.Trigger is "lock" or "unlock"))
            failures.Add(UiText.Get("AutoLock") + "/" + UiText.Get("AutoUnlock"));
        ConfigurePcSampling(enabled);
        ConfigureSystemEvents(enabled, failures);
        Unavailable = string.Join(", ", failures);
    }

    private void OnForeground(nint eventHook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time) =>
        dispatcher.TryEnqueue(() =>
        {
            if (disposed || suspended || hook != eventHook) return;
            ApplyForeground();
        });

    private void ApplyForeground()
    {
        if (foreground.Observe(ForegroundPath(), Environment.TickCount64) is not { } change) return;
        _ = engine.TriggerAsync(rule => rule.Trigger switch
        {
            "foreground" => string.Equals(rule.Argument, change.Current, StringComparison.OrdinalIgnoreCase),
            "foreground-exit" => string.Equals(rule.Argument, change.Previous, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    private static string ForegroundPath()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var id);
        return id == 0 ? "" : ProcessPath((int)id);
    }

    private void OnWindowMessage(uint message, nuint value, nint data)
    {
        if (message == 0x0218 && value == 0x8013 && data != 0)
        {
            // The power payload is only valid during the window procedure.
            if (Marshal.ReadInt32(data, 16) == sizeof(int))
            {
                var setting = Marshal.PtrToStructure<PowerSetting>(data);
                dispatcher.TryEnqueue(() => ApplyPowerSetting(setting));
            }
            return;
        }
        // Run outside the window procedure so cloud calls never block shell messages.
        dispatcher.TryEnqueue(() =>
        {
            if (disposed) return;
            if (message == 0x0312 && hotkeys.Contains((uint)value)) _ = engine.TriggerAsync("hotkey", ((char)value).ToString());
            if (message == 0x02B1 && value == 7) _ = engine.TriggerAsync("lock");
            if (message == 0x02B1 && value == 8) _ = engine.TriggerAsync("unlock");
            if (message == 0x0218 && value == 4)
            {
                suspended = true;
                ResetPcSampling();
                recovery.Clear();
                UpdateSampleTimer();
                engine.Cancel();
            }
            if (message == 0x0218 && value == 18 && suspended)
            {
                suspended = false;
                foreground.Reset(ForegroundPath());
                ResetPcSampling();
                QueueRecovery("wake");
                UpdateSampleTimer();
            }
        });
    }

    private void StopForeground()
    {
        watchForeground = false;
        foreground.Reset("");
        if (hook != 0) UnhookWinEvent(hook);
        hook = 0;
    }

    public void Dispose()
    {
        disposed = true;
        engine.RulesChanged -= Reconfigure;
        host.WindowMessage -= OnWindowMessage;
        sampleTimer.Stop();
        DisposeSystemEvents();
        StopForeground();
        foreach (var key in hotkeys) UnregisterHotKey(host.Handle, (int)key);
        hotkeys.Clear();
        if (sessionNotifications) WTSUnRegisterSessionNotification(host.Handle);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ForegroundCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, ForegroundCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
}
