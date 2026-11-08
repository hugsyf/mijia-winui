using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace MiHome.WinUI;

public sealed partial class TrayMenuWindow : Window
{
    private MenuFlyout Menu => (MenuFlyout)Anchor.Resources["TrayMenu"];
    private bool closed;
    private bool pending;
    public TrayMenuWindow()
    {
        InitializeComponent();
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, args) => { if (!App.IsDesktopExiting) { args.Cancel = true; Menu.Hide(); AppWindow.Hide(); } };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; }
        Closed += (_, _) => closed = true;
        Menu.Closed += (_, _) => { if (!closed) AppWindow.Hide(); };
        Anchor.Loaded += (_, _) => ShowFlyout();
    }
    internal void ShowMenu((RectInt32 Anchor, RectInt32 WorkArea, double Scale) location)
    {
        pending = true;
        Anchor.RequestedTheme = Infrastructure.AppPreferences.Load(App.SettingsDirectory).Theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        AppWindow.MoveAndResize(new(location.Anchor.X, location.Anchor.Y, 1, 1));
        AppWindow.Show(); Activate();
        if (Anchor.XamlRoot is not null) ShowFlyout();
    }
    private void ShowFlyout() { if (!pending) return; pending = false; Menu.ShowAt(Anchor); }
    private void OnOpen(object sender, RoutedEventArgs args) { Menu.Hide(); App.ShowMainWindow(); }
    private void OnSettings(object sender, RoutedEventArgs args) { Menu.Hide(); App.ShowDesktopSettings(); }
    private void OnExit(object sender, RoutedEventArgs args) { Menu.Hide(); App.ExitDesktop(); }
}
