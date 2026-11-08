using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class LicenseCatalogPage : UserControl
{
    public LicenseCatalogPage() { InitializeComponent(); Catalog.ItemsSource = LicensesView.LoadCatalog(); }
    private void OnExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (sender.DataContext is not ComponentLicense item || sender.Content is not StackPanel body) return;
        ((TextBlock)body.Children[1]).Text = LicensesView.ReadTerms(item);
        if (Uri.TryCreate(item.Source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        { var source = (HyperlinkButton)body.Children[2]; source.NavigateUri = uri; source.Visibility = Visibility.Visible; }
    }
}
