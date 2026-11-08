using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.ViewModels;

namespace MiHome.WinUI.Controls;

public sealed partial class QuickDeviceTile : UserControl
{
    private DeviceTile? tile;
    private PropertySchema? power;
    private AdapterWidget? widget;
    private bool editing;
    internal event Action<DeviceTile>? DetailsRequested;
    internal event Action<CardWriteRequestedEventArgs>? WriteRequested;
    public QuickDeviceTile()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (tile is not null) tile.SnapshotChanged -= Update; tile = null; };
    }
    private void Subscribe()
    {
        if (tile is not null) tile.SnapshotChanged -= Update;
        tile = DataContext as DeviceTile;
        if (tile is not null) tile.SnapshotChanged += Update;
        Update();
    }
    private void Update()
    {
        if (tile is null) return;
        DeviceIcon.Tile = tile; DeviceName.Text = tile.Name;
        ToolTipService.SetToolTip(MainButton, tile.Name + " · " + tile.Room + "\n" + tile.StateHint);
        power = null; widget = null;
        if (tile.State is { } state)
        {
            if (tile.Device.Adapter is { } adapter)
            {
                widget = adapter.Card.FirstOrDefault(w => w.Kind == "toggle" && AdapterConditions.Matches(adapter, state, w.When));
                if (widget is not null) power = adapter.Bindings.FirstOrDefault(b => b.Id == widget.Binding)?.Property;
            }
            else
            {
                var powers = DeviceExperience.PowerProperties(tile.Device, state.Schema);
                if (powers.Count == 1) power = powers[0];
            }
        }
        var on = power is not null && DeviceCardPresentation.TryValue(tile.State, power, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
        MainButton.IsChecked = on;
        DetailsButton.IsChecked = on;
        ToolTipService.SetToolTip(DetailsButton, UiText.Format("TrayDeviceDetails", tile.Name));
        AutomationProperties.SetName(DetailsButton, UiText.Format("TrayDeviceDetails", tile.Name));
        MainButton.IsEnabled = !editing && (power is null || DeviceExperience.CanWrite(tile.Device, tile.State, power, !on) &&
            AdapterConditions.Matches(tile.Device.Adapter, tile.State, widget?.EnabledWhen));
        AutomationProperties.SetName(MainButton, power is null ? UiText.Format("TrayDeviceDetails", tile.Name) : UiText.Format(on ? "PowerOffDevice" : "PowerOnDevice", tile.Name));
    }
    internal void RefreshState() => Update();
    internal void SetEditing(bool value)
    {
        editing = value; IsHitTestVisible = !value;
        MainButton.IsTabStop = DetailsButton.IsTabStop = !value;
        DetailsButton.IsEnabled = !value;
        Update();
    }
    private void OnMainClick(object sender, RoutedEventArgs args)
    {
        if (tile is null || editing) return;
        if (power is null) { MainButton.IsChecked = false; DetailsRequested?.Invoke(tile); return; }
        if (AdapterConditions.Matches(tile.Device.Adapter, tile.State, widget?.EnabledWhen))
            WriteRequested?.Invoke(new(tile, power, MainButton.IsChecked == true));
    }
    private void OnDetailsClick(object sender, RoutedEventArgs args)
    {
        Update();
        if (!editing && tile is not null) DetailsRequested?.Invoke(tile);
    }
}
