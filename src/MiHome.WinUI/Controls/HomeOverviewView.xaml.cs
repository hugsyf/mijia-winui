using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed partial class HomeOverviewView : UserControl
{
    internal event Action<string?, bool>? LightingCommandRequested;
    internal event Action<DeviceTile>? DeviceRequested;
    internal event Action<HomeEnvironment>? SourceRequested;
    internal event Action<HomeLighting>? LightingRequested;
    internal event Action? PinsRequested;
    public string Headline { get; private set; } = "";
    private HomeOverview? model;
    public HomeOverviewView() => InitializeComponent();
    internal void Configure(DataTemplate template, Style itemStyle) { Favorites.ItemTemplate = template; Favorites.ItemContainerStyle = itemStyle; }
    internal void Show(HomeOverview next)
    {
        model = next;
        var on = next.Lighting.Sum(room => room.On);
        var unknown = next.Lighting.Sum(room => room.Unknown);
        Headline = string.Join(" · ", new[] {next.Lighting.Count > 0 ? Infrastructure.UiText.Format("OnCount",on) : "", unknown > 0 ? Infrastructure.UiText.Format("UnknownCount",unknown) : "", next.Attention.Count > 0 ? Infrastructure.UiText.Format("AttentionCount",next.Attention.Count) : ""}.Where(text => text.Length > 0));
        AttentionItems.ItemsSource = next.Attention.Select(issue => new HomeAttentionRow(issue)).ToList();
        AttentionItems.Visibility = Visible(next.Attention.Count > 0);
        EnvironmentSection.Visibility = Visible(next.Environment.Count > 0);
        var sources = next.Environment.Select(room => room.Source?.State?.ReadAt).Where(time => time.HasValue).ToList();
        Updated.Text = sources.Count > 0 ? Infrastructure.UiText.Format("UpdatedAt",sources.Max()!.Value.ToLocalTime().ToString("HH:mm")) : "";
        EnvironmentItems.ItemsSource = next.Environment.Select(room => new HomeEnvironmentRow(room)).ToList();
        SensorSection.Visibility = Visible(next.Sensors.Count > 0);
        SensorItems.ItemsSource = next.Sensors.Select(tile => new HomeDeviceRow(tile)).ToList();
        EmptyPins.Visibility = Visible(next.Pins.Count == 0);
        Favorites.Visibility = Visible(next.Pins.Count > 0);
        Favorites.ItemsSource = next.Pins.Select(tile => tile with { PresentationSize = "compact", IsEditing = false }).ToList();
        LightingSection.Visibility = Visible(next.Lighting.Count > 0);
        LightingItems.ItemsSource = next.Lighting.Select((room, index) => new HomeLightingRow(room, index, next.Lighting.Count)).ToList();
        AllLightingPower.IsChecked = on > 0 ? true : unknown > 0 ? null : false;
        var powerHint = Infrastructure.UiText.Format(on==0&&unknown==0?"AllLightsOn":"AllLightsOff",Infrastructure.UiText.Get("WholeHome"));
        ToolTipService.SetToolTip(AllLightingPower, powerHint); AutomationProperties.SetName(AllLightingPower, powerHint);
        AutomationProperties.SetItemStatus(AllLightingPower, Infrastructure.UiText.Get(unknown>0?"UnknownState":on>0?"LightsOn":"AllOff"));
        NetworkSection.Visibility = Visible(next.Network.Count > 0);
        NetworkItems.ItemsSource = next.Network.Select(item => new HomeDeviceRow(item.Tile)).ToList();
        ResizeColumns();
    }
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void ResizeColumns()
    {
        var right = model is { } m && m.Lighting.Count + m.Network.Count > 0;
        var wide = ActualWidth >= 920 && right;
        LeftColumn.Width = new GridLength(wide ? 1.35 : 1, GridUnitType.Star);
        RightColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Columns.ColumnSpacing = wide ? 22 : 0; Columns.RowSpacing = !wide && right ? 22 : 0;
        Grid.SetColumn(RightSections, wide ? 1 : 0); Grid.SetRow(RightSections, wide ? 0 : 1);
    }
    private void OnColumnsSizeChanged(object sender, SizeChangedEventArgs args) => ResizeColumns();
    private void OnPinsClick(object sender, RoutedEventArgs args) => PinsRequested?.Invoke();
    private void OnAttentionClick(object sender, RoutedEventArgs args) { if (((FrameworkElement)sender).DataContext is HomeAttentionRow row) DeviceRequested?.Invoke(row.Tile); }
    private void OnDeviceClick(object sender, RoutedEventArgs args) { if (((FrameworkElement)sender).DataContext is HomeDeviceRow row) DeviceRequested?.Invoke(row.Tile); }
    private void OnEnvironmentClick(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is not HomeEnvironmentRow row) return;
        if (row.Model.Source is { } source) DeviceRequested?.Invoke(source); else SourceRequested?.Invoke(row.Model);
    }
    private void OnSourceClick(object sender, RoutedEventArgs args) { if (((FrameworkElement)sender).DataContext is HomeEnvironmentRow row) SourceRequested?.Invoke(row.Model); }
    private void OnLightingClick(object sender, RoutedEventArgs args) { if (((FrameworkElement)sender).DataContext is HomeLightingRow row) LightingRequested?.Invoke(row.Model); }
    private void OnLightingPowerClick(object sender, RoutedEventArgs args) { if (((FrameworkElement)sender).DataContext is HomeLightingRow row) LightingCommandRequested?.Invoke(row.Model.RoomId, row.Model.On == 0 && row.Model.Unknown == 0); }
    private void OnAllLightingClick(object sender, RoutedEventArgs args) { if (model is { } m) LightingCommandRequested?.Invoke(null, m.Lighting.All(room => room.On == 0 && room.Unknown == 0)); }
    private void OnFavoriteClick(object sender, ItemClickEventArgs args) { if (!Favorites.ConsumeControlInteraction()) DeviceRequested?.Invoke((DeviceTile)args.ClickedItem); }
    private void ResizeFavorites()
    {
        if (Favorites.ItemsPanelRoot is VariableSizedWrapGrid panel && Favorites.ActualWidth > 0)
        { var columns = Math.Clamp((int)(Favorites.ActualWidth / 240), 1, 3); panel.MaximumRowsOrColumns = columns; panel.ItemWidth = Favorites.ActualWidth / columns; }
    }
    private void OnFavoritesSizeChanged(object sender, SizeChangedEventArgs args) => ResizeFavorites();
    private void OnFavoritesLoaded(object sender, RoutedEventArgs args) => ResizeFavorites();
}

public sealed class HomeAttentionRow
{
    public DeviceTile Tile { get; }
    public string Message { get; }
    internal HomeAttentionRow(HomeAttention issue) { Tile = issue.Tile; Message = issue.Tile.Name + " · " + issue.Message; }
}
public sealed class HomeEnvironmentRow
{
    internal HomeEnvironment Model { get; }
    public string Room => Model.Room;
    public string Temperature => Model.Temperature is { } n ? n.ToString("0.#") : "—";
    public string Unit => Model.Temperature.HasValue ? " °C" : "";
    public string Humidity => Model.Humidity is { } h ? Infrastructure.UiText.Format("HumidityValue",h.ToString("0.#")) : Infrastructure.UiText.Get("HumidityMissing");
    public string Hint => (Model.Source?.Name ?? "选择传感器") + (Model.Candidates.Count > 1 ? "；右键更换主传感器" : "");
    public Visibility ChooseVisibility => Model.Source is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReadingVisibility => Model.Source is null ? Visibility.Collapsed : Visibility.Visible;
    internal HomeEnvironmentRow(HomeEnvironment model) => Model = model;
}
public sealed class HomeDeviceRow(DeviceTile tile)
{
    public DeviceTile Tile => tile;
    public string Name => tile.Name;
    public string Summary => tile.Device.Online && tile.Summary.Length > 0 ? Infrastructure.UiText.State(tile.Summary) : Infrastructure.UiText.State("离线或状态未知");
    public string Hint => tile.Name + " · " + tile.Room + " · " + tile.StateHint;
    public string Connected => (AdapterReadings.Capability(tile.Device, tile.State, "connected") ?? DeviceCardPresentation.Number(tile.State, "router", "connected-device-number")) is { } n ? Infrastructure.UiText.Format("ConnectedCount",n.ToString("0")) : "";
    public string Speed => string.Join("   ", new[] {DeviceCardPresentation.RouterSpeed(tile.State, "download-speed") is { } down ? "↓ " + down : "", DeviceCardPresentation.RouterSpeed(tile.State, "upload-speed") is { } up ? "↑ " + up : ""}.Where(s => s.Length > 0));
}
public sealed class HomeLightingRow
{
    internal HomeLighting Model { get; }
    public string Room => Model.Room;
    public string Summary => Model.Unknown > 0 ? Infrastructure.UiText.Format("LightUnknown",Model.On,Model.Unknown) : Model.On==0?Infrastructure.UiText.Get("AllOff"):Infrastructure.UiText.Format("LightCount",Model.On,Model.On+Model.Off);
    public bool? Checked => Model.On > 0 ? true : Model.Unknown > 0 ? null : false;
    public string PowerHint => Infrastructure.UiText.Format(Model.On==0&&Model.Unknown==0?"AllLightsOn":"AllLightsOff",Room);
    public DeviceTile Icon { get; }
    public Thickness Separator { get; }
    public CornerRadius Corners { get; }
    internal HomeLightingRow(HomeLighting model, int index, int count)
    {
        Model = model; Separator = new Thickness(0, 0, 0, index < count - 1 ? 1 : 0);
        Corners = new CornerRadius(index == 0 ? 8 : 0, index == 0 ? 8 : 0, index == count - 1 ? 8 : 0, index == count - 1 ? 8 : 0);
        Icon = new(new("home-light", model.Room, "xiaomi.switch.w1", "", "", model.Room, true) { SubclassId = "5", CardCategory = "light", CardIcon = "ceiling-light" });
    }
}
