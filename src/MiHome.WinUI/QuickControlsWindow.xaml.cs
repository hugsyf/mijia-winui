using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace MiHome.WinUI;

public sealed partial class QuickControlsWindow : Window
{
    private readonly DesktopSession session;
    private readonly ObservableCollection<DeviceTile> tiles = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Windows.UI.ViewManagement.UISettings uiSettings = new();
    private ExpandedCardView? expanded;
    private DeviceTile? selected;
    private Dialogs.TrayDevicesDialog? chooser;
    private (RectInt32 Anchor, RectInt32 WorkArea, double Scale) placement;
    private string? homeId;
    private bool rendering, modal, closed, pendingChoose;
    private int motionVersion, lastHeight;
    private long deactivatedAt;
    internal bool IsOpen { get; private set; }
    internal bool WasJustDeactivated => Environment.TickCount64 - deactivatedAt < 250;
    internal QuickControlsWindow(DesktopSession session)
    {
        this.session = session;
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, args) => { if (!App.IsDesktopExiting) { args.Cancel = true; Dismiss(); } };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true; }
        Devices.ItemsSource = tiles;
        Devices.SizeChanged += (_, _) => { if (Devices.ItemsPanelRoot is ItemsWrapGrid grid) grid.ItemWidth = Math.Max(80, Math.Floor(Devices.ActualWidth / 3)); };
        session.Changed += OnSessionChanged;
        Surface.Loaded += async (_, _) => { if (pendingChoose) { pendingChoose = false; await SafeAsync(ChooseAsync); } };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
                DispatcherQueue.TryEnqueue(CheckForeground);
        };
        Closed += (_, _) => { closed = true; StopForegroundWatch(); timer.Stop(); session.Changed -= OnSessionChanged; };
        timer.Tick += async (_, _) => { if (IsOpen && selected is null && Edit.IsChecked != true && !modal) await SafeAsync(RefreshQuickAsync); };
    }
    internal void ShowPanel((RectInt32 Anchor, RectInt32 WorkArea, double Scale) location, bool chooseDevices)
    {
        placement = location; lastHeight = 0; motionVersion++; IsOpen = true;
        Surface.RequestedTheme = AppPreferences.Load(App.SettingsDirectory).Theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        ((Storyboard)Surface.Resources["CloseMotion"]).Stop(); Body.Opacity = 1; Motion.Y = 0;
        UpdateView(); Position(); AppWindow.Show(); Activate();
        ActivatePopup();
        if (uiSettings.AnimationsEnabled) ((Storyboard)Surface.Resources["OpenMotion"]).Begin();
        if (Families.IsEnabled && Families.Visibility == Visibility.Visible) Families.Focus(FocusState.Programmatic);
        else if (tiles.Count > 0) Devices.Focus(FocusState.Programmatic);
        else EmptyAction.Focus(FocusState.Programmatic);
        var refresh = AppPreferences.Load(App.SettingsDirectory).RefreshSeconds;
        if (refresh > 0) { timer.Interval = TimeSpan.FromSeconds(Math.Max(10, refresh)); timer.Start(); }
        if (chooseDevices)
        {
            if (Surface.XamlRoot is null) pendingChoose = true; else _ = SafeAsync(ChooseAsync);
        }
        else _ = SafeAsync(RefreshQuickAsync);
    }
    internal async void Dismiss()
    {
        if (!IsOpen || closed) return;
        IsOpen = false; StopForegroundWatch(); timer.Stop(); chooser?.Hide();
        speakerPanel?.ClearDraft();
        var version = ++motionVersion;
        ((Storyboard)Surface.Resources["OpenMotion"]).Stop();
        if (uiSettings.AnimationsEnabled)
        { ((Storyboard)Surface.Resources["CloseMotion"]).Begin(); await Task.Delay(110); }
        if (!closed && version == motionVersion) AppWindow.Hide();
    }
    private void OnSessionChanged()
    {
        if (closed) return;
        UpdateView();
        if (IsOpen) Position();
    }
    private void UpdateView()
    {
        rendering = true;
        try
        {
            if (homeId != session.Home?.Id)
            {
                homeId = session.Home?.Id; selected = null; expanded = null; ExpandedHost.Content = null;
                Edit.IsChecked = false; chooser?.Hide();
            }
            Families.ItemsSource = session.Homes;
            Families.SelectedItem = session.Home;
            Families.Visibility = session.Homes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            FamilyLabel.Text = session.Home?.Name ?? "";
            FamilyLabel.Visibility = session.Homes.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
            Families.IsEnabled = session.Homes.Count > 1 && !session.MainBusy && Edit.IsChecked != true;
            var current = tiles.ToDictionary(t => t.Device.Id);
            var desired = session.Pinned.Select(next =>
            {
                if (current.TryGetValue(next.Device.Id, out var old) && old.Device == next.Device)
                { old.UpdateSnapshot(next.State); return old; }
                return next;
            }).ToList();
            // Native drag reordering owns the collection until editing finishes.
            if (Edit.IsChecked != true) CollectionReconciler.Apply(tiles, desired, t => t.Device.Id);
            var editing = Edit.IsChecked == true;
            var busy = session.Busy || session.MainBusy;
            Loading.IsActive = busy;
            Devices.IsEnabled = session.Authenticated && session.Backend.Running && !busy;
            Refresh.IsEnabled = session.Authenticated && session.Home is not null && !busy && !editing;
            Edit.IsEnabled = session.Authenticated && session.Home is not null && !busy && selected is null;
            Choose.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            Choose.IsEnabled = !busy;
            Devices.CanDragItems = Devices.CanReorderItems = Devices.AllowDrop = editing;
            for (var i = 0; i < Devices.Items.Count; i++)
                if (Devices.ContainerFromIndex(i) is GridViewItem { ContentTemplateRoot: QuickDeviceTile tile }) tile.SetEditing(editing);
            Empty.Visibility = tiles.Count == 0 && selected is null ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = UiText.Get(!session.Authenticated ? "TraySignIn" : busy ? "TrayLoading" : session.Home is null ? "TrayNoHome" : "TrayEmpty");
            EmptyAction.Content = UiText.Get(session.Authenticated && session.Home is not null ? "TrayChooseDevices" : "TrayOpenMain");
            EmptyAction.IsEnabled = !busy;
            Devices.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
            Details.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateTraySpeaker(editing, busy);
            if (selected is { } selection)
            {
                var next = session.Available.FirstOrDefault(t => t.Device.Id == selection.Device.Id);
                if (next is null) { OnBack(this, new RoutedEventArgs()); return; }
                selected = next;
                expanded?.Update(next);
                DetailSummary.Text = next.Name + " · " + UiText.State(next.Summary);
                ExpandedHost.IsEnabled = !busy && session.Authenticated;
            }
        }
        finally { rendering = false; }
    }
    private void Position()
    {
        var scale = Math.Max(0.5, placement.Scale);
        var work = placement.WorkArea;
        var width = Math.Min((int)(360 * scale), work.Width - 16);
        var speakerHeight = SpeakerSection.Visibility == Visibility.Visible ? Math.Max(55, SpeakerSection.ActualHeight) : 0;
        var noticeHeight = Feedback.IsOpen ? Feedback.ActualHeight + Feedback.Margin.Bottom : 0;
        var tileHeight = tiles.Count == 0 ? 120 : ((tiles.Count + 2) / 3) * 84;
        var height = Math.Min((int)Math.Ceiling(((selected is null ? 130 + tileHeight + speakerHeight : 480) + noticeHeight) * scale), work.Height - 24);
        if (height == lastHeight) return;
        lastHeight = height;
        var x = Math.Clamp(placement.Anchor.X + placement.Anchor.Width - width, work.X + 8, Math.Max(work.X + 8, work.X + work.Width - width - 8));
        var y = placement.Anchor.Y < work.Y ? work.Y + 8 : work.Y + work.Height - height - 8;
        AppWindow.MoveAndResize(new(x, y, width, height));
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { Message(error.Message); }
    }
    private void Message(string? text)
    {
        if (closed || string.IsNullOrWhiteSpace(text)) return;
        Feedback.Message = UiText.State(text); Feedback.IsOpen = true;
    }
    private void OnTileLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not QuickDeviceTile tile) return;
        tile.DetailsRequested -= ShowDetails; tile.DetailsRequested += ShowDetails;
        tile.WriteRequested -= OnTileWrite; tile.WriteRequested += OnTileWrite;
        tile.SetEditing(Edit.IsChecked == true);
    }
    private async void OnTileWrite(CardWriteRequestedEventArgs args)
    {
        if (Edit.IsChecked == true) return;
        await SafeAsync(async () => Message(await session.WriteAsync(args.Tile, args.Property, args.Value)));
    }
    private void ShowDetails(DeviceTile tile)
    {
        if (Edit.IsChecked == true) return;
        selected = tile;
        expanded = tile.State is null ? null : new ExpandedCardView(tile, ExpandedCardPlan.Build(tile.Device, tile.State.Schema), session.QuickService(tile.Device.Id));
        if (expanded is not null) { expanded.StateRead += session.CapturePublisher(); expanded.Feedback += Message; }
        ExpandedHost.Content = expanded;
        UpdateView(); lastHeight = 0; Position();
    }
    private void OnBack(object sender, RoutedEventArgs args)
    { selected = null; expanded = null; ExpandedHost.Content = null; UpdateView(); lastHeight = 0; if (IsOpen) Position(); }
    private void OnOpenApp(object sender, RoutedEventArgs args)
    { if (selected is { } tile) session.OpenDevice(tile); else App.ShowMainWindow(); }
    private void OnSettings(object sender, RoutedEventArgs args) => App.ShowDesktopSettings();
    private void OnFamilyChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!rendering && Families.SelectedItem is Home home && home.Id != session.Home?.Id) session.SelectHome(home.Id);
    }
    private async void OnRefresh(object sender, RoutedEventArgs args) => await SafeAsync(RefreshQuickAsync);
    private void OnEdit(object sender, RoutedEventArgs args)
    {
        UpdateView();
        if (IsOpen) Position();
        for (var i = 0; i < Devices.Items.Count; i++)
                if (Devices.ContainerFromIndex(i) is GridViewItem { ContentTemplateRoot: QuickDeviceTile tile }) tile.SetEditing(Edit.IsChecked == true);
    }
    private async void OnDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (Edit.IsChecked != true || tiles.Select(t => t.Device.Id).SequenceEqual(session.Pins)) return;
        await SafeAsync(() => session.SavePinsAsync(tiles.Select(t => t.Device.Id)));
    }
    private async void OnChoose(object sender, RoutedEventArgs args) => await SafeAsync(ChooseAsync);
    private async void OnEmptyAction(object sender, RoutedEventArgs args)
    { if (session.Authenticated && session.Home is not null) await SafeAsync(ChooseAsync); else App.ShowMainWindow(); }
    private async Task ChooseAsync()
    {
        if (modal || !session.Authenticated || session.Home is null || Surface.XamlRoot is null) return;
        modal = true; var home = session.Home.Id;
        try
        {
            chooser = new Dialogs.TrayDevicesDialog { XamlRoot = Surface.XamlRoot, RequestedTheme = Surface.ActualTheme };
            var rows = session.Available.Select(t => new Dialogs.ChoiceItem(t.Device.Id, t.Name + " · " + t.Room)).ToList();
            chooser.ChoiceList.ItemsSource = rows;
            foreach (var row in rows.Where(r => session.Pins.Contains((string)r.Value))) chooser.ChoiceList.SelectedItems.Add(row);
            if (await chooser.ShowAsync() != ContentDialogResult.Primary || session.Home?.Id != home) return;
            var selectedIds = chooser.ChoiceList.SelectedItems.Cast<Dialogs.ChoiceItem>().Select(r => (string)r.Value).ToList();
            var ordered = session.Pins.Where(selectedIds.Contains).Concat(selectedIds.Where(id => !session.Pins.Contains(id))).ToList();
            Edit.IsChecked = false;
            await SafeAsync(() => session.SavePinsAsync(ordered));
        }
        finally { chooser = null; modal = false; UpdateView(); lastHeight = 0; if (IsOpen) Position(); }
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    { if (args.Key == VirtualKey.Escape) { args.Handled = true; if (selected is not null) OnBack(sender, new RoutedEventArgs()); else Dismiss(); } }
}
