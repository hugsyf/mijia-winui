using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private bool detailFull;
    private string? draggedCard;
    private bool savingOrder;
    private void OnEditModeChanged(object sender, RoutedEventArgs e)
    {
        if (busy) { EditModeButton.IsChecked = browser.IsEditing; return; }
        var requested = EditModeButton.IsChecked == true;
        if (Navigation.SelectedItem != Navigation.MenuItems[1]) Navigation.SelectedItem = Navigation.MenuItems[1];
        if (detailOpen) Back();
        browser.IsEditing = requested;
        EditModeButton.IsChecked = requested;
        EditModeButton.Content = browser.IsEditing ? "完成" : "编辑";
        DeviceList.CanDragItems = DeviceList.AllowDrop = browser.IsEditing;
        ApplyFilter();
    }
    private void OnCardDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        var resizingCard = Enumerable.Range(0, DeviceList.Items.Count).Any(i =>
            DeviceList.ContainerFromIndex(i) is GridViewItem container && container.ContentTemplateRoot is DeviceCardView { ResizeInProgress: true });
        if (!browser.IsEditing || busy || savingOrder || resizingCard || args.Items.FirstOrDefault() is not DeviceTile tile)
        { args.Cancel = true; return; }
        draggedCard = tile.Device.Id;
        args.Data.SetData("MiHome.Card", tile.Device.Id);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }
    private void OnCardDragCompleted(object sender, DragItemsCompletedEventArgs args)
    {
        var moved = draggedCard;
        draggedCard = null;
        if (args.DropResult == DataPackageOperation.Move && moved is not null)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusCard(moved));
    }
    private void OnCardDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = browser.IsEditing && draggedCard is not null && !busy && !savingOrder ? DataPackageOperation.Move : DataPackageOperation.None;
        args.Handled = true;
    }
    private async void OnCardDrop(object sender, DragEventArgs args)
    {
        var moved = draggedCard;
        if (!browser.IsEditing || moved is null || busy) return;
        args.Handled = true;
        var point = args.GetPosition(DeviceList);
        GridViewItem? target = null;
        DeviceTile? tile = null;
        var nearest = double.MaxValue;
        for (var i = 0; i < DeviceList.Items.Count; i++)
        {
            if (DeviceList.Items[i] is not DeviceTile candidate || candidate.Device.Id == moved ||
                DeviceList.ContainerFromIndex(i) is not GridViewItem container) continue;
            var origin = container.TransformToVisual(DeviceList).TransformPoint(new());
            var dx = point.X - origin.X - container.ActualWidth / 2;
            var dy = point.Y - origin.Y - container.ActualHeight / 2;
            var distance = dx * dx + dy * dy;
            if (distance < nearest) { nearest = distance; target = container; tile = candidate; }
        }
        if (target is null || tile is null) return;
        var local = args.GetPosition(target);
        var after = local.Y > target.ActualHeight * .75 || local.Y >= target.ActualHeight * .25 && local.X > target.ActualWidth / 2;
        await SaveOrderAsync(moved, tile.Device.Id, after);
    }
    private async Task SaveOrderAsync(string moved, string target, bool after)
    {
        if (savingOrder || Homes.SelectedItem is not Home home) return;
        var previous = browser.OrderedIds;
        var ids = DeviceOrdering.Move(previous, moved, target, after);
        if (ids.SequenceEqual(previous)) return;
        savingOrder = true;
        browser.Reorder(ids);
        if (draggedCard is null) FocusCard(moved);
        try
        {
            await backend.CallAsync("SetCardOrder", new { homeId = home.Id, deviceIds = ids });
        }
        catch
        {
            if ((Homes.SelectedItem as Home)?.Id == home.Id)
            {
                browser.Reorder(previous);
                ShowMessage("无法保存卡片顺序，已恢复原来的排列。请重试。");
            }
        }
        finally { savingOrder = false; }
    }
    private async void OnCardOrderKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!browser.IsEditing || busy || args.Key is not (VirtualKey.Up or VirtualKey.Down) ||
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        DependencyObject? focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (focused is not null && focused is not GridViewItem) focused = VisualTreeHelper.GetParent(focused);
        if (focused is not GridViewItem item || DeviceList.ItemFromContainer(item) is not DeviceTile tile) return;
        var index = DeviceList.Items.IndexOf(tile) + (args.Key == VirtualKey.Up ? -1 : 1);
        if (index < 0 || index >= DeviceList.Items.Count || DeviceList.Items[index] is not DeviceTile target) return;
        args.Handled = true;
        await SaveOrderAsync(tile.Device.Id, target.Device.Id, args.Key == VirtualKey.Down);
    }
    private void FocusCard(string id) => DispatcherQueue.TryEnqueue(() =>
    {
        DeviceList.UpdateLayout();
        for (var i = 0; i < DeviceList.Items.Count; i++)
            if (DeviceList.Items[i] is DeviceTile tile && tile.Device.Id == id && DeviceList.ContainerFromIndex(i) is GridViewItem container)
            { container.Focus(FocusState.Programmatic); break; }
    });
    private void OnExpandDetail(object sender, RoutedEventArgs e)
    {
        var previousWidth = DrawerPane.ActualWidth;
        detailFull = !detailFull;
        ApplyDetailPresentation();
        ShowCurrentView();
        ResizeDrawerAnimated(previousWidth);
    }
    private void ApplyDetailPresentation()
    {
        if (!detailOpen) return;
        var width = Math.Max(1, ContentArea.ActualWidth - ContentArea.Padding.Left - ContentArea.Padding.Right);
        var inline = false;
        var paneWidth = DrawerLayout.Width(width, detailFull);
        // Explicit grid columns keep inline content measured in the remaining space.
        // Overlay uses our own scrim, with no native popup to retain input capture.
        DeviceListHost.Visibility = detailFull ? Visibility.Collapsed : Visibility.Visible;
        DrawerColumn.Width = new GridLength(inline ? paneWidth : 0);
        Grid.SetColumn(DrawerPane, inline ? 1 : 0);
        Grid.SetColumnSpan(DrawerPane, inline ? 1 : 2);
        DrawerPane.Width = paneWidth;
        DrawerPane.HorizontalAlignment = HorizontalAlignment.Right;
        DrawerPane.Visibility = Visibility.Visible;
        DrawerScrim.Visibility = !inline && !detailFull ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.MaxWidth = detailFull ? 920 : double.PositiveInfinity;
        DetailTitle.MaxWidth = double.PositiveInfinity;
        ExpandDetailButton.Content = new FontIcon { Glyph = detailFull ? "\uE73F" : "\uE740", FontSize = 16 };
        var action = detailFull ? "收起为抽屉" : "展开为整页";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ExpandDetailButton, action);
        ToolTipService.SetToolTip(ExpandDetailButton, action);
        (specializedPanel as Panels.DeviceExperiencePanel)?.SetExpanded(detailFull);
        genericPanel?.SetExpanded(detailFull);
        (specializedPanel as Panels.AdapterPanel)?.SetExpanded(detailFull);
        GenericExpander.Visibility = detailFull && GenericContent.Content is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!detailFull) { CardAppearance.Visibility = DeviceInformation.Visibility = Visibility.Collapsed; }
    }
    private void FocusDrawer()
    {
        if (detailOpen) CloseDetailButton.Focus(FocusState.Programmatic);
    }
    private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        if (detailOpen) { Back(); args.Handled = true; }
        else if (browser.IsEditing) { EditModeButton.IsChecked = false; OnEditModeChanged(EditModeButton, new()); args.Handled = true; }
    }
}
