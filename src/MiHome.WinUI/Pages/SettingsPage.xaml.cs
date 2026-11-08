using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class SettingsPage : UserControl
{
    internal ListView NavigationList => Sections;
    internal SelectorBar NavigationTabs => Tabs;
    internal ContentControl ToolbarHost => Toolbar;
    internal ContentControl ContentHost => Body;
    internal ScrollViewer ScrollHost => Scroller;
    public SettingsPage() => InitializeComponent();
    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 760;
        Sections.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        TabScroll.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        NavigationColumn.Width = new GridLength(narrow ? 0 : 180);
        Layout.ColumnSpacing = narrow ? 0 : 28;
    }
}
