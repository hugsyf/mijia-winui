using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class GuideToolbar : UserControl
{
    public event Action? BackRequested;
    public string Markdown { get; set; } = "";
    public GuideToolbar() => InitializeComponent();
    internal void FocusBack() => Back.Focus(FocusState.Programmatic);
    private void OnBack(object sender, RoutedEventArgs args) => BackRequested?.Invoke();
    private void OnCopy(object sender, RoutedEventArgs args) => DocumentPresenter.Copy(Markdown);
}
