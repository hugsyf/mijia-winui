using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MiHome.WinUI.Dialogs;

public sealed record DeviceReadingRow(string Label, string Value);

public sealed partial class DeviceReadingsDialog : ContentDialog
{
    public DeviceReadingsDialog() => InitializeComponent();

    internal void SetReadings(string deviceName, IReadOnlyList<DeviceReadingRow> rows, string status, double maximumHeight)
    {
        DeviceName.Text = deviceName;
        Status.Text = status;
        Status.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReadingsScroll.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReadingsScroll.MaxHeight = maximumHeight;
        Readings.ItemsSource = rows;
    }
}
