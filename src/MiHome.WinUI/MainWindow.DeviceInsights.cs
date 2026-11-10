using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using MiHome.WinUI.Core;
using MiHome.WinUI.Dialogs;
using MiHome.WinUI.Infrastructure;

namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private sealed record ConsumableReading(string Label, string? Value);
    private sealed record EnergyReading(long Time, double KilowattHours);

    private async void OnDeviceConsumables(object sender, RoutedEventArgs args) => await ShowDeviceReadingsAsync(false);
    private async void OnDeviceEnergyHistory(object sender, RoutedEventArgs args) => await ShowDeviceReadingsAsync(true);

    private async Task ShowDeviceReadingsAsync(bool energy)
    {
        if (!detailOpen || selectedDevice is not { } device || diagnosticService is not null) return;
        var generation = viewGeneration;
        await RunAsync(async () =>
        {
            DeviceReadingRow[] rows = [];
            var status = UiText.Get(energy ? "DeviceEnergyEmpty" : "DeviceConsumablesEmpty");
            try
            {
                var result = await backend.CallAsync(energy ? "GetDeviceEnergyHistory" : "GetDeviceConsumables",
                    new { deviceId = device.Id, homeId = device.HomeId });
                rows = energy ? EnergyRows(result) : (result.Deserialize<ConsumableReading[]>(BackendClient.Json) ?? [])
                    .Select(reading => new DeviceReadingRow(reading.Label, reading.Value ?? "—")).ToArray();
            }
            catch (BridgeException error) when (error.Code != "auth")
            {
                status = UiText.Get("DeviceReadingsFailed");
            }

            if (closing || !detailOpen || generation != viewGeneration || Root.XamlRoot is not { } root) return;
            var dialog = new DeviceReadingsDialog
            {
                XamlRoot = root,
                RequestedTheme = Root.ActualTheme,
                Title = UiText.Get(energy ? "DeviceEnergyTitle" : "DeviceConsumablesMenu.Text")
            };
            dialog.SetReadings(device.Name, rows, status, Math.Clamp(root.Size.Height - 240, 120, 420));
            await dialog.ShowAsync();
        });
    }

    private static DeviceReadingRow[] EnergyRows(JsonElement result)
    {
        var culture = CultureInfo.GetCultureInfo(UiText.Language);
        return (result.Deserialize<EnergyReading[]>(BackendClient.Json) ?? [])
            .Select(reading => new DeviceReadingRow(DateTimeOffset.FromUnixTimeSeconds(reading.Time).ToLocalTime().ToString("Y", culture),
                UiText.Format("DeviceEnergyValue", reading.KilowattHours))).ToArray();
    }
}
