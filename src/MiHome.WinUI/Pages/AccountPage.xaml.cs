using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class AccountPage : UserControl
{
    public event Action? SyncRequested;
    public event RoutedEventHandler? LogoutRequested;
    internal string FamilyName { get => Family.Text; set => Family.Text = value; }
    public AccountPage() => InitializeComponent();
    private void OnSync(object sender, RoutedEventArgs args) => SyncRequested?.Invoke();
    private void OnLogout(object sender, RoutedEventArgs args) => LogoutRequested?.Invoke(sender, args);
}
