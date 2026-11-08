using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
namespace MiHome.WinUI;
public partial class App : Application
{
    private Window? window;
    internal static bool StartupDiagnostic => Environment.GetCommandLineArgs().Any(a => a is "--diagnose-startup" or "--diagnose-layout" or "--diagnose-drawer" or "--preview-workspace" or "--preview-adapter" or "--preview-adapter-manager");
    public App()
    {
        Infrastructure.UiText.Configure(Infrastructure.AppPreferences.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiHomeWinUI")).Language);
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride=Infrastructure.UiText.Language;
        if (StartupDiagnostic)
            DebugSettings.BindingFailed += (_, e) => RecordStartupStage("Binding: " + e.Message);
        UnhandledException += (_, e) => RecordStartupFailure(e.Exception);
        InitializeComponent();
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (!StartupDiagnostic && !await RegisterInstanceAsync()) { Exit(); return; }
            RecordStartupStage("Creating window");
            window = new MainWindow();
            if (StartupDiagnostic)
            {
                RecordStartupStage("Validating device card icon resources");
                foreach (var category in new[] { "light", "switch", "wireless-switch", "outlet", "camera", "router",
                    "temperature-humidity-sensor", "occupancy-sensor", "humidifier", "toothbrush", "air-fryer",
                    "computer", "television", "fan", "heater", "curtain", "speaker", "appliance" }.Concat(ViewModels.DeviceCardPresentation.ConfiguredIcons))
                    _ = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry),
                        ViewModels.DeviceCardPresentation.IconPath(category));
                var commandLine = Environment.GetCommandLineArgs();
                var managerIndex=Array.IndexOf(commandLine,"--preview-adapter-manager");
                if(managerIndex>=0)
                {
                    if(managerIndex+1>=commandLine.Length)throw new ArgumentException("Preview fixture path is required.");
                    await ((MainWindow)window).PrepareAdapterManagerPreviewAsync(commandLine[managerIndex+1]);window.Activate();return;
                }
                var adapterIndex=Array.IndexOf(commandLine,"--preview-adapter");
                if(adapterIndex>=0)
                {
                    if(adapterIndex+2>=commandLine.Length)throw new ArgumentException("Preview and resolved adapter paths are required.");
                    await ((MainWindow)window).PrepareAdapterPreviewAsync(commandLine[adapterIndex+1],commandLine[adapterIndex+2]);
                    window.Activate();return;
                }
                var previewIndex = Array.IndexOf(commandLine, "--preview-workspace");
                if (previewIndex >= 0)
                {
                    if (previewIndex + 1 >= commandLine.Length) throw new ArgumentException("A public schema fixture path is required.");
                    await ((MainWindow)window).PrepareWorkspacePreviewAsync(commandLine[previewIndex + 1]);
                    window.Activate(); return;
                }
                var layoutIndex = Array.IndexOf(commandLine, "--diagnose-layout");
                if (layoutIndex >= 0)
                {
                    if (layoutIndex + 1 >= commandLine.Length) throw new ArgumentException("A public schema fixture path is required.");
                    await ((MainWindow)window).ValidateDeviceLayoutsAsync(commandLine[layoutIndex + 1]);
                }
                var drawerIndex = Array.IndexOf(commandLine, "--diagnose-drawer");
                if (drawerIndex >= 0)
                {
                    if (drawerIndex + 1 >= commandLine.Length) throw new ArgumentException("A public schema fixture path is required.");
                    // Overlay light-dismiss requires a real, active XamlRoot.
                    window.Activate();
                    await ((MainWindow)window).ValidateDrawerFlowAsync(commandLine[drawerIndex + 1]);
                }
                RecordStartupStage(drawerIndex >= 0 ? "Drawer lifecycle regression completed with an active mock window" : "Window initialization completed without activation");
                window.Close();
                Exit();
                return;
            }
            RecordStartupStage("Activating window");
            var main = (MainWindow)window;
            var trayReady = InitializeTray(main);
            var hidden = Environment.GetCommandLineArgs().Contains("--startup") &&
                Infrastructure.AppPreferences.Load(SettingsDirectory).StartHidden && trayReady && !pendingActivation;
            if (!hidden) ShowMainWindow();
            await main.InitializeDesktopAsync();
            RecordStartupStage("Window activated");
        }
        catch (Exception exception)
        {
            RecordStartupFailure(exception);
            throw;
        }
    }
    private static string StartupLogPath(string name) => Path.Combine(
        StartupDiagnostic ? Path.Combine(AppContext.BaseDirectory, "diagnostics") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiHomeWinUI"), name);
    private static string StartupContext => $"{DateTimeOffset.Now:O} | Version {typeof(App).Assembly.GetName().Version}";
    internal static void RecordStartupStage(string stage)
    {
        try
        {
            var path = StartupLogPath("startup-stage.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (StartupDiagnostic) File.AppendAllText(path, $"{StartupContext} | {stage}\n");
            else File.WriteAllText(path, $"{StartupContext} | {stage}\n");
        }
        catch { }
    }
    private static void RecordStartupFailure(Exception exception)
    {
        // Save the managed error first. Native error lookup must not prevent logging.
        try
        {
            var path = StartupLogPath("startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{StartupContext}\n{exception}\n");
        }
        catch { }
        try
        {
            if (GetRestrictedErrorInfo(out var info) == 0 && info is not null)
            {
                info.GetErrorDetails(out var description, out var error, out var restricted, out _);
                File.AppendAllText(StartupLogPath("startup-error.log"),
                    $"Native error 0x{error:X8}: {description}\n{restricted}\n");
            }
        }
        catch { }
    }
    [DllImport("combase.dll")] private static extern int GetRestrictedErrorInfo(out IRestrictedErrorInfo? info);
    [ComImport, Guid("82BA7092-4C88-427D-A7BC-16DD93FEB67E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRestrictedErrorInfo
    {
        void GetErrorDetails([MarshalAs(UnmanagedType.BStr)] out string description, out int error,
            [MarshalAs(UnmanagedType.BStr)] out string restrictedDescription, [MarshalAs(UnmanagedType.BStr)] out string capabilitySid);
        void GetReference([MarshalAs(UnmanagedType.BStr)] out string reference);
    }
}
