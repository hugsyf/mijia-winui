using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed class CardWriteRequestedEventArgs(DeviceTile tile, PropertySchema property, object value) : EventArgs
{
    public DeviceTile Tile { get; } = tile;
    public PropertySchema Property { get; } = property;
    public object Value { get; } = value;
}
public sealed class CardResizeRequestedEventArgs(DeviceTile tile, string size) : EventArgs
{
    public DeviceTile Tile { get; } = tile;
    public string Size { get; } = size;
}
public sealed partial class DeviceCardView : UserControl
{
    private DeviceTile? preparedTile;
    private Popup? resizePreview;
    private bool resizing;
    private IDeviceService? service;
    private ExpandedCardView? expanded;
    private DeviceTile? subscribed;
    private Action<DeviceCardState>? stateRead;
    private Action<string>? feedback;
    public void Configure(IDeviceService service,Action<DeviceCardState> stateRead,Action<string>? feedback=null)
    {
        if(ReferenceEquals(this.service,service))return;
        this.service=service;this.stateRead=stateRead;this.feedback=feedback;preparedTile=null;EnsureContent();
    }
    public bool ResizeInProgress => resizing;
    internal int ResizeHandleCount => ResizeHost.Content is CardResizeThumb ? 1 : 0;
    public DeviceTile? Tile => DataContext as DeviceTile;
    public event EventHandler<CardWriteRequestedEventArgs>? WriteRequested;
    public event EventHandler<CardResizeRequestedEventArgs>? ResizeRequested;
    public DeviceCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => {Subscribe();EnsureContent();};
        Loaded += (_, _) => {Subscribe();EnsureContent();};
        Unloaded += (_, _) => {if(subscribed is not null)subscribed.SnapshotChanged-=OnSnapshotChanged;subscribed=null;CancelResize();};
    }
    private void Subscribe(){if(subscribed is not null)subscribed.SnapshotChanged-=OnSnapshotChanged;subscribed=Tile;if(subscribed is not null)subscribed.SnapshotChanged+=OnSnapshotChanged;}
    private void OnSnapshotChanged(){if(expanded is not null&&Tile is {} tile)expanded.Update(tile);else Render();}
    private static TextBlock Line(string value, bool strong = false)
    {
        var text = DeviceUi.Text(Infrastructure.UiText.State(value), strong ? "BodyStrongTextBlockStyle" : "DeviceSecondaryTextStyle");
        text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(text, value);
        return text;
    }
    internal void EnsureContent()
    {
        if (ReferenceEquals(preparedTile, Tile) && Content is not null) return;
        if(expanded is not null && preparedTile is {} previous && Tile is {} next && previous.Device==next.Device && previous.Size==next.Size && previous.IsEditing==next.IsEditing)
        {preparedTile=next;expanded.Update(next);return;}
        CancelResize();
        preparedTile = Tile;
        expanded=null;
        Render();
    }
    private ToggleButton PowerButton(DeviceTile tile, PropertySchema property, bool named)
    {
        var valid = tile.State is { } state && DeviceCardPresentation.TryValue(state, property, out _);
        var on = valid && DeviceCardPresentation.TryValue(tile.State, property, out var value) && value.GetBoolean();
        var label = DeviceModels.ChannelLabel(tile.Device, property.Siid);
        var button = new ToggleButton { Content = named ? label + (on ? " 开" : " 关") : new FontIcon { Glyph = "\uE7E8", FontSize = 16 },
            IsChecked = on, MinWidth = named ? 60 : 36, Height = 36, Padding = new Thickness(named ? 6 : 0, 0, named ? 6 : 0, 0),
            IsEnabled = !tile.IsEditing && valid && DeviceExperience.CanWrite(tile.Device, tile.State, property, !on), VerticalAlignment = VerticalAlignment.Center };
        if (!named) { button.Width = 36; button.CornerRadius = new CornerRadius(18); }
        var action = $"{(on ? "关闭" : "开启")} {tile.Name}" + (named ? " " + label : "");
        AutomationProperties.SetName(button, action); ToolTipService.SetToolTip(button, action);
        button.Click += (_, _) => WriteRequested?.Invoke(this, new(tile, property, button.IsChecked == true));
        return button;
    }
    private void Render()
    {
        if (Tile is not { } tile) { CardRoot.Visibility = Visibility.Collapsed; return; }
        CardRoot.Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, tile.AccessibleName);
        var size = tile.Size;
        ExpandedHost.Content = null; ExpandedHost.Visibility = Visibility.Collapsed;
        CardSurface.Visibility = Visibility.Visible;
        ResizeHost.Content = tile.IsEditing ? CreateResizeHandle(tile, size) : null;
        if(size=="expanded" && tile.State is {} snapshot && service is not null && ExpandedCardPlan.Build(tile.Device,snapshot.Schema) is {Count:>0} items)
        {
            expanded=new(tile,items,service);expanded.StateRead+=s=>stateRead?.Invoke(s);expanded.Feedback+=m=>feedback?.Invoke(m);
            CardSurface.Visibility=Visibility.Collapsed;ExpandedHost.Visibility=Visibility.Visible;ExpandedHost.Content=expanded;return;
        }
        if(tile.Device.Adapter is not null) { RenderAdapter(tile,size); return; }
        var kind = DeviceExperience.Kind(tile.Device, tile.State?.Schema);
        var powers = tile.State is { } state ? DeviceExperience.PowerProperties(tile.Device, state.Schema) : [];
        var singlePower = powers.Count == 1 && !DeviceExperience.IsWireless(tile.Device, tile.State, powers[0].Siid) ? powers[0] : null;
        var subtitle = !tile.Device.Online ? tile.Status : size == "compact" && tile.Summary.Length > 0
            ? tile.Room + " · " + Infrastructure.UiText.State(tile.Summary) : tile.Room;
        if (size == "expanded" && kind == ExperienceKind.TemperatureHumidity &&
            DeviceCardPresentation.Number(tile.State, "battery", "battery-level") is { } battery)
            subtitle += $" · 电量 {battery:0}%";
        ConfigureShell(tile, size, subtitle, singlePower);
        if (size != "compact")
        {
            FrameworkElement detail;
            if (tile.Summary is "缺水" or "设备异常，请查看详情") detail = Line(tile.Summary);
            else if (kind == ExperienceKind.TemperatureHumidity)
                detail = Metrics(tile, ("温度", "temperature-humidity-sensor", "temperature", "°C"), ("湿度", "temperature-humidity-sensor", "relative-humidity", "%"));
            else if (kind == ExperienceKind.Humidifier)
            {
                if (size == "expanded") detail = Metrics(tile, ("当前湿度", "environment", "relative-humidity", "%"), ("目标湿度", "humidifier", "target-humidity", "%"));
                else
                {
                    var readings=new HumidifierCardSummary();
                    readings.SetReading(DeviceCardPresentation.Power(tile.State,"humidifier") is {} on?on?"已开启":"已关闭":"状态未知",
                        Metrics(tile,("当前湿度","environment","relative-humidity","%")));detail=readings;
                }
            }
            else if (powers.Count > 1)
            {
                var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var property in powers)
                {
                    var wireless = DeviceExperience.IsWireless(tile.Device, tile.State, property.Siid);
                    if (wireless) channels.Children.Add(new TextBlock { Text = DeviceModels.ChannelLabel(tile.Device, property.Siid) + "\n无线", FontSize = 12 });
                    else channels.Children.Add(PowerButton(tile, property, true));
                }
                detail = channels;
            }
            else if (kind == ExperienceKind.Outlet && size == "expanded")
                detail = Metrics(tile, ("当前功率", "power-consumption", "electric-power", "W"));
            else detail = Line(tile.Device.Online ? tile.Summary : tile.Status);
            CardDetail.Content = detail;
        }
    }
    private PropertySchema? cardPowerProperty;
    private AdapterCondition? cardPowerCondition;
    private void ConfigureShell(DeviceTile tile, string size, string subtitle, PropertySchema? property)
    {
        CardIcon.Tile = tile;
        CardName.Text = tile.Name; CardSubtitle.Text = Infrastructure.UiText.State(subtitle);
        ToolTipService.SetToolTip(CardName, tile.Name); ToolTipService.SetToolTip(CardSubtitle, subtitle);
        CardDetail.Content = null;
        CardDetail.Visibility = size == "compact" ? Visibility.Collapsed : Visibility.Visible;
        CardBody.RowSpacing = size == "compact" ? 0 : 12;
        CardBody.VerticalAlignment = size == "compact" ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        CardBody.Margin = size == "compact" ? new Thickness(0) : new Thickness(0, 0, 0, 8);
        CardSurface.Padding = size == "compact" ? new Thickness(16, 12, 20, 20) : new Thickness(16);
        ToolTipService.SetToolTip(CardSurface, tile.StateHint + (tile.Status.Length > 0 ? "\n" + tile.Status : ""));
        cardPowerCondition=null;cardPowerProperty = property;
        CardPower.Visibility = property is null ? Visibility.Collapsed : Visibility.Visible;
        if (property is null) return;
        var valid = DeviceCardPresentation.TryValue(tile.State, property, out var value);
        var on = valid && value.GetBoolean();
        CardPower.IsChecked = on;
        CardPower.IsEnabled = !tile.IsEditing && valid && DeviceExperience.CanWrite(tile.Device, tile.State, property, !on);
        var action = Infrastructure.UiText.Format(on?"PowerOffDevice":"PowerOnDevice",tile.Name);
        AutomationProperties.SetName(CardPower, action); ToolTipService.SetToolTip(CardPower, action);
    }
    private void OnPowerClick(object sender, RoutedEventArgs args)
    { if (Tile is { } tile && cardPowerProperty is { } property && AdapterConditions.Matches(tile.Device.Adapter,tile.State,cardPowerCondition)) WriteRequested?.Invoke(this, new(tile, property, CardPower.IsChecked == true)); }
    private void CancelResize()
    {
        resizing = false;
        if (resizePreview is not null) resizePreview.IsOpen = false;
        resizePreview = null;
    }
    internal void FocusResizeHandle() => (ResizeHost.Content as CardResizeThumb)?.Handle.Focus(FocusState.Programmatic);
    private CardResizeThumb CreateResizeHandle(DeviceTile tile, string start)
    {
        var thumb = new CardResizeThumb();
        AutomationProperties.SetName(thumb, "调整 " + tile.Name + " 卡片大小");
        AutomationProperties.SetName(thumb.Handle, "调整 " + tile.Name + " 卡片大小");
        AutomationProperties.SetItemStatus(thumb.Handle, "当前尺寸：" + start);
        AutomationProperties.SetHelpText(thumb, "拖动调整；左或上键紧凑，下键标准，右键展开；Esc 取消拖动");
        ToolTipService.SetToolTip(thumb, "拖动调整大小；方向键也可选择尺寸");
        double cellWidth = 260, dx = 0, dy = 0;
        int columns = 1;
        var desired = start;
        bool moved = false;
        FrameworkElement? outline = null;
        TextBlock? label = null;
        thumb.Handle.DragStarted += (_, _) =>
        {
            CancelResize(); resizing = true; dx = dy = 0; moved = false; desired = start;
            AutomationProperties.SetItemStatus(thumb.Handle, "正在调整大小");
            DependencyObject? parent = this;
            while (parent is not null && parent is not GridViewItem) parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            if (parent is GridViewItem container && ItemsControl.ItemsControlFromItemContainer(container) is GridView view &&
                view.ItemsPanelRoot is VariableSizedWrapGrid panel)
            { cellWidth = panel.ItemWidth; columns = Math.Max(1, panel.MaximumRowsOrColumns); }
            var point = TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
            var preview = new ResizePreviewView(); label=preview.SizeLabel; outline=preview;
            resizePreview = new Popup { XamlRoot = XamlRoot, IsHitTestVisible = false, IsLightDismissEnabled = false,
                HorizontalOffset = point.X, VerticalOffset = point.Y, Child = outline };
        };
        thumb.Handle.DragDelta += (_, e) =>
        {
            if (!resizing || outline is null || label is null) return;
            dx += e.HorizontalChange; dy += e.VerticalChange;
            if (Math.Abs(dx) + Math.Abs(dy) < 6) return;
            moved = true;
            desired = DeviceCardSizing.FromDrag(start, cellWidth, dx, dy, columns);
            outline.Width = Math.Min(columns, DeviceCardSizing.Columns(desired)) * cellWidth - 12;
            outline.Height = DeviceCardSizing.Rows(desired) * DeviceCardSizing.RowHeight - 12;
            label.Text = desired switch { "compact" => "紧凑", "standard" => "标准", _ => "展开" };
            AutomationProperties.SetItemStatus(thumb.Handle, "目标尺寸：" + desired);
            if (resizePreview is not null) resizePreview.IsOpen = true;
        };
        thumb.Handle.DragCompleted += (_, e) =>
        {
            var commit = resizing && moved && !e.Canceled && desired != start;
            CancelResize();
            AutomationProperties.SetItemStatus(thumb.Handle, "当前尺寸：" + (commit ? desired : start));
            if (commit) ResizeRequested?.Invoke(this, new(tile, desired));
        };
        thumb.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && resizing) { CancelResize(); e.Handled = true; return; }
            if (resizing || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
            var size = e.Key switch
            {
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Up => "compact",
                Windows.System.VirtualKey.Down => "standard", Windows.System.VirtualKey.Right => "expanded", _ => null
            };
            if (size is null) return;
            e.Handled = true;
            if (size != start) ResizeRequested?.Invoke(this, new(tile, size));
        };
        return thumb;
    }
    private static CardMetricsView Metrics(DeviceTile tile, params (string Label, string Service, string Property, string Unit)[] readings)
    {
        var view=new CardMetricsView();
        view.SetReadings(readings.Select(reading=>
        {
            var value=tile.Device.Online?DeviceCardPresentation.Number(tile.State,reading.Service,reading.Property):null;
            return new CardMetric(reading.Label,value is {} n?n.ToString("0.#",CultureInfo.CurrentCulture):"—",value is null?"":" "+reading.Unit);
        }).ToList());
        return view;
    }
}
