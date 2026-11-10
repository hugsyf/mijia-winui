using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace MiHome.WinUI.Dialogs;
public sealed partial class AdapterImportDialog : ContentDialog
{
    internal ListView FileList => Files;
    internal ToggleSwitch ApplyToggle => ApplyModels;
    internal TextBlock WarningLabel => Warning;
    internal event Action<AdapterImportRow, ContentControl>? PreviewRequested;
    public AdapterImportDialog() => InitializeComponent();
    internal void SetMaximumHeight(double height) => Files.MaxHeight = Math.Clamp(height - 72, 80, 360);
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is AdapterImportRow row) args.ItemContainer.IsEnabled = row.CanInstall;
    }
    private void OnPreview(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: AdapterImportRow row } button || FlyoutBase.GetAttachedFlyout(button) is not Flyout flyout) return;
        var width = Math.Max(280, Math.Min(560, XamlRoot.Size.Width - 48));
        // The content and the presenter must share the same available width.
        ((ScrollViewer)flyout.Content).MaxWidth = width - 32;
        var host = (ContentControl)((ScrollViewer)flyout.Content).Content;
        PreviewRequested?.Invoke(row, host);
        flyout.ShowAt(button);
    }
}
public sealed record AdapterImportRow(object Value, string Title, string Detail, bool CanInstall)
{
    public Visibility PreviewVisibility => CanInstall ? Visibility.Visible : Visibility.Collapsed;
    public string PreviewLabel => "预览 " + Title;
}
