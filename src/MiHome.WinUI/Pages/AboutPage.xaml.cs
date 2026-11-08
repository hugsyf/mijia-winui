using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class AboutPage : UserControl
{
    public event Action<string>? CommandRequested;
    public event Action<bool>? AutomaticUpdatesChanged;
    internal bool CheckAutomatically {get=>AutomaticUpdates.IsOn;set=>AutomaticUpdates.IsOn=value;}
    internal void SetUpdateStatus(string text)=>UpdateRow.Description=text;
    internal void SetUpdateChecking(bool checking)
    {
        CheckUpdatesButton.IsEnabled = !checking;
        UpdateProgress.IsActive = checking;
        UpdateProgress.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnCheckUpdates(object sender,RoutedEventArgs args)=>CommandRequested?.Invoke("updates");
    private void OnAutomaticUpdates(object sender,RoutedEventArgs args)=>AutomaticUpdatesChanged?.Invoke(AutomaticUpdates.IsOn);
    public AboutPage()
    {
        InitializeComponent();
        Version.Text = Infrastructure.UiText.Format("VersionLabel",typeof(App).Assembly.GetName().Version);
        Runtime.Text = $"WinUI 3 · .NET {Environment.Version}";
    }
    private void OnChanges(object sender, RoutedEventArgs args) => CommandRequested?.Invoke("changes");
    private void OnDiagnostics(object sender, RoutedEventArgs args) => CommandRequested?.Invoke("diagnostics");
    private void OnPrivacy(object sender, RoutedEventArgs args) => CommandRequested?.Invoke("privacy");
    private void OnLicenses(object sender, RoutedEventArgs args) => CommandRequested?.Invoke("licenses");
}
