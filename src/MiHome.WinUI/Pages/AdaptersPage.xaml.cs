using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using MiHome.WinUI.Core;
using MiHome.WinUI.ViewModels;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class AdaptersPage : UserControl
{
    internal AppBarButton ImportButton => Import;
    internal AppBarButton ExportButton => Export;
    internal AppBarButton RefreshButton => Refresh;
    internal AppBarButton GuideButton => Guide;
    internal AppBarToggleButton SelectionToggle => Select;
    internal SelectorBar FilterBar => Filter;
    internal ListView PackageList => Packages;
    internal TextBlock CountLabel => Count;
    internal ToggleSwitch EnableSelection => SelectionEnabled;
    internal CheckBox SelectAllButton => SelectAll;
    internal Button RemoveSelectedButton => RemoveSelected;
    internal InfoBar ResultBar => Result;
    internal event Action<string, bool>? SortRequested;
    internal event Action<AdapterPackage, string, bool>? PackageCommand;
    internal event Action? PackageSelectionChanged;
    private string sort = "name";
    public AdaptersPage() => InitializeComponent();
    private void OnDone(object sender,RoutedEventArgs args)=>Select.IsChecked=false;
    internal void SetSelectionMode(bool choosing, int count, bool filtered)
    {
        CatalogToolbar.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        SelectionBar.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = UiText.Get(filtered ? "AdapterNoFilteredPackages" : "NoAdapters");
    }
    internal void SetCounts(IReadOnlyList<AdapterPackage> packages)
    {
        Filter.Items[0].Text=UiText.Get("All")+" "+packages.Count;
        Filter.Items[1].Text=UiText.Get("Enabled")+" "+packages.Count(p=>p.Enabled);
        Filter.Items[2].Text=UiText.Get("Disabled")+" "+packages.Count(p=>!p.Enabled);
    }
    private void OnDetailsExpanding(Expander sender,ExpanderExpandingEventArgs args)
    {
        if(sender.DataContext is not AdapterPackageRow row||sender.Content is not StackPanel body||body.Children[1] is not ContentControl host||host.Content is not null)return;
        host.Content=row.Package.Readme.Length>0?DocumentPresenter.Build(row.Package.Readme,compact:true):DocumentPresenter.Plain(UiText.Get("NoReadme"));
    }
    private void OnSort(object sender, RoutedEventArgs args)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string key }) sort = key;
        SortRequested?.Invoke(sort, Descending.IsChecked);
    }
    private void OnEnabled(object sender, RoutedEventArgs args)
    {
        if (sender is ToggleSwitch { DataContext: AdapterPackageRow row } toggle && toggle.IsLoaded && toggle.IsOn != row.Enabled)
            PackageCommand?.Invoke(row.Package, "enable", toggle.IsOn);
    }
    private void OnPackageSelection(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { DataContext: AdapterPackageRow { Choosing: true } row } checkbox) return;
        row.IsSelected = checkbox.IsChecked == true;
        PackageSelectionChanged?.Invoke();
    }
    private void Send(object sender, string command)
    { if (((FrameworkElement)sender).DataContext is AdapterPackageRow row) PackageCommand?.Invoke(row.Package, command, false); }
    private void OnInfo(object sender, RoutedEventArgs args) => Send(sender, "info");
    private void OnRollback(object sender, RoutedEventArgs args) => Send(sender, "rollback");
    private void OnRemove(object sender, RoutedEventArgs args) => Send(sender, "remove");
}
public sealed class AdapterPackageRow : ObservableObject
{
    private bool isSelected;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public AdapterPackage Package { get; }
    public bool Choosing {get;}
    public Visibility SelectionVisibility => Choosing ? Visibility.Visible : Visibility.Collapsed;
    public string Name=>Package.Name;
    public string Models=>string.Join("、",Package.Models);
    public string Version=>"v"+Package.Version;
    public string SdkVersion=>Package.SdkVersion.Length>0?Package.SdkVersion:UiText.Get("Unspecified");
    public string Author=>Package.Author.Length>0?Package.Author:"—";
    public string State=>UiText.Get(Package.Enabled?"Enabled":"Disabled");
    public string Title => Package.Name + "  " + Package.Version;
    public string Metadata => string.Join("、", Package.Models) + " · SDK " + (Package.SdkVersion.Length > 0 ? Package.SdkVersion : "未声明") + (Package.Enabled ? "" : " · 已停用");
    public bool Enabled => Package.Enabled;
    public string EnableLabel => UiText.Format("EnablePackage",Package.Name);
    public string RemoveLabel => UiText.Format("RemovePackage",Package.Name);
    public Visibility CommandsVisibility { get; }
    public Visibility RollbackVisibility => Package.CanRollback ? Visibility.Visible : Visibility.Collapsed;
    public DeviceTile Icon { get; }
    internal AdapterPackageRow(AdapterPackage package, bool choosing)
    {
        Choosing=choosing;Package = package; CommandsVisibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        var identity = new DeviceIdentity("adapter-icon", package.Name, package.Models.FirstOrDefault() ?? "", "", "", "", true);
        if (package.IconFile.Length > 0) identity = identity with { Adapter = new DeviceAdapter(package.Id, package.Name, package.Version, "appliance", [], [], [], [], package.IconFile) };
        Icon = new(identity);
    }
}
