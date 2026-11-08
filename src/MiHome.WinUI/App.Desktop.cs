using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MiHome.WinUI.Infrastructure;

namespace MiHome.WinUI;

public partial class App
{
    internal static DesktopSession Session { get; } = new();
    private SingleInstanceGate? singleInstance;
    private TrayIconService? tray;
    private QuickControlsWindow? quickControls;
    private TrayMenuWindow? trayMenu;
    private bool exiting, mainClosed, pendingActivation;
    internal static bool IsDesktopExiting => ((App)Current).exiting;
    internal static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiHomeWinUI");
    private async Task<bool> RegisterInstanceAsync()
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        singleInstance = await SingleInstanceGate.StartAsync(() => dispatcher.TryEnqueue(ShowMainWindow),
            Environment.GetCommandLineArgs().Contains("--startup"));
        return singleInstance is not null;
    }
    private bool InitializeTray(MainWindow main)
    {
        main.Closed += (_, _) => { mainClosed = true; ExitDesktop(); };
        main.AppWindow.Closing += (_, args) =>
        {
            if (!exiting && tray?.Ready == true && AppPreferences.Load(SettingsDirectory).CloseToTray)
            { args.Cancel = true; main.AppWindow.Hide(); }
        };
        try
        {
            tray = new TrayIconService();
            tray.Click += () => main.DispatcherQueue.TryEnqueue(() => OpenQuickPanel());
            tray.DoubleClick += () => main.DispatcherQueue.TryEnqueue(ShowMainWindow);
            tray.Unavailable += () => main.DispatcherQueue.TryEnqueue(() => { ShowMainWindow(); main.ShowDesktopMessage(UiText.Get("TrayUnavailable")); });
            tray.ShutdownRequested += () => main.DispatcherQueue.TryEnqueue(ExitDesktop);
            tray.ContextMenu += () => main.DispatcherQueue.TryEnqueue(() =>
            {
                if (tray is null) return;
                quickControls?.Dismiss();
                trayMenu ??= new TrayMenuWindow();
                trayMenu.ShowMenu(tray.Placement());
            });
            return true;
        }
        catch (Exception error)
        {
            RecordStartupStage("Tray initialization: " + error.GetType().Name);
            main.ShowDesktopMessage(UiText.Get("TrayUnavailable"));
            return false;
        }
    }
    internal static void ShowMainWindow()
    {
        var app = (App)Current;
        if (app.window is not MainWindow main) { app.pendingActivation = true; return; }
        app.quickControls?.Dismiss();
        if (main.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
        main.AppWindow.Show(); main.Activate();
    }
    internal static void ShowDesktopSettings()
    {
        ShowMainWindow();
        if (((App)Current).window is MainWindow main) main.OpenDesktopSettings();
    }
    internal static void OpenQuickPanel(bool chooseDevices = false)
    {
        var app = (App)Current;
        if (app.tray?.Ready != true) { ShowMainWindow(); return; }
        if (!chooseDevices && app.quickControls?.WasJustDeactivated == true) return;
        app.quickControls ??= new QuickControlsWindow(Session);
        if (app.quickControls.IsOpen && !chooseDevices) { app.quickControls.Dismiss(); return; }
        app.quickControls.ShowPanel(app.tray.Placement(), chooseDevices);
    }
    internal static void ExitDesktop()
    {
        var app = (App)Current;
        if (app.exiting) return;
        app.exiting = true;
        app.tray?.Dispose(); app.tray = null;
        Session.Dispose();
        app.quickControls?.Close(); app.trayMenu?.Close();
        if (!app.mainClosed) app.window?.Close();
        app.singleInstance?.Dispose(); app.singleInstance = null;
        app.Exit();
    }
}
