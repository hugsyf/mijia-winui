using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using MiHome.WinUI.Core;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;
using MiHome.WinUI.ViewModels;
using QRCoder;
using Windows.Storage.Streams;
namespace MiHome.WinUI;

public sealed partial class MainWindow : Window
{
    private BackendClient backend => App.Session.Backend;
    private readonly PanelRegistry panels = new();
    private readonly GenericPanelProvider generic = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DeviceBrowserViewModel browser => App.Session.Browser;
    private bool authenticated { get => App.Session.Authenticated; set => App.Session.Authenticated = value; }
    private bool ready, busy, active = true, closing;
    private int viewGeneration;
    private int refreshingDetailGeneration = -1;
    private long loginSession;
    private GenericPanel? genericPanel;
    private IRefreshablePanel? specializedPanel;
    private bool settingsSelected;
    private bool licensesOpen;
    private LicensesView? licensesView;
    private bool detailOpen;
    private bool sessionChecking = true;
    private bool settingCardAppearance;
    private bool changingRooms;
    private DeviceIdentity? selectedDevice;
    private readonly Microsoft.UI.System.ThemeSettings themeSettings;
    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiHomeWinUI");
    public MainWindow()
    {
        App.RecordStartupStage("Loading window XAML");
        InitializeComponent();
        App.RecordStartupStage("Extending content into title bar");
        ExtendsContentIntoTitleBar = true;
        App.RecordStartupStage("Setting title bar drag region");
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        App.RecordStartupStage("Subscribing to title bar theme changes");
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
        themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(AppWindow.Id);
        themeSettings.Changed += OnSystemThemeChanged;
        App.RecordStartupStage("Applying title bar theme");
        UpdateTitleBarTheme();
        App.RecordStartupStage("Binding device collections");
        Notice.Closed += (_, _) => Notice.Visibility = Visibility.Collapsed;
        CardAppearance.Collapsed += (_, _) => CardAppearance.Visibility = Visibility.Collapsed;
        DeviceInformation.Collapsed += (_, _) => DeviceInformation.Visibility = Visibility.Collapsed;
        DeviceList.ItemsSource = browser.Tiles;
        InitializeHome();
        App.RecordStartupStage("Sizing window");
        var scale = Math.Max(1, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(1180 * scale, workArea.Width), (int)Math.Min(820 * scale, workArea.Height)));
        App.RecordStartupStage("Creating Mica backdrop");
        SystemBackdrop = new MicaBackdrop();
        InitializeSettings();
        panels.Register(generic);
        panels.Register(new DeviceExperienceProvider());
        backend.EventReceived += message => DispatcherQueue.TryEnqueue(() => HandleEvent(message));
        backend.Disconnected += () => DispatcherQueue.TryEnqueue(() => { if (!closing) { App.Automation.Cancel(); browser.SetCardStates([]); ApplyFilter(); Restart.Visibility = Visibility.Visible; ShowMessage(MiHome.WinUI.Infrastructure.UiText.Get("S0bf618e12dda")); } });
        AutomationView.Configure(PickAutomationProgramAsync);
        timer.Tick += async (_, _) => { if (active && !busy && detailOpen && (DevicesView.Visibility == Visibility.Visible || OverviewView.Visibility == Visibility.Visible)) await RefreshDetailAsync(); };
        Activated += (_, e) =>
        {
            var wasActive = active;
            active = e.WindowActivationState != WindowActivationState.Deactivated;
            if (active && !wasActive && ready && !App.StartupDiagnostic) LoadDesktopPreferences();
            if (active && !wasActive && ready && !busy && detailOpen && (DevicesView.Visibility == Visibility.Visible || OverviewView.Visibility == Visibility.Visible))
                _ = RefreshDetailAsync();
        };
        Closed += (_, _) => { closing = true; themeSettings.Changed -= OnSystemThemeChanged; timer.Stop(); };
        App.Session.Updated += OnSharedStateUpdated;
        App.Session.AuthenticationLost += OnSharedAuthenticationLost;
        App.Session.OpenDeviceRequested += OnTrayDeviceRequested;
        App.Session.HomeRequested += OnTrayHomeRequested;
        Root.Loaded += async (_, _) => { if (!App.StartupDiagnostic) await InitializeDesktopAsync(); };
    }
    private Task? desktopInitialization;
    internal Task InitializeDesktopAsync() => desktopInitialization ??= InitializeDesktopCoreAsync();
    private async Task InitializeDesktopCoreAsync()
    {
        ready = true; LoadTheme(); Navigation.SelectedItem = Navigation.MenuItems[0];
        await RunAsync(StartAsync);
        if (preferences.CheckUpdates) _ = CheckForUpdatesAsync(true);
    }
    private void OnSharedStateUpdated() { if (!closing) ApplyFilter(); }
    private async void OnSharedAuthenticationLost()
    {
        ResetSession();
        try { await backend.CallAsync("Logout"); } catch { }
    }
    private void OnTrayDeviceRequested(DeviceTile tile)
    {
        if (busy) return;
        App.ShowMainWindow();
        Navigation.SelectedItem = Navigation.MenuItems[1];
        _ = RunAsync(() => OpenDeviceAsync(tile.Device));
    }
    private async void OnTrayHomeRequested(string id)
    {
        if (busy) return;
        await RunAsync(async () =>
        {
            Homes.SelectedItem = (Homes.ItemsSource as List<Home>)?.FirstOrDefault(h => h.Id == id);
            await LoadDevicesAsync();
        });
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy || closing) return;
        busy = true;
        App.Session.MainBusy = true; App.Session.Notify();
        Progress.Visibility = sessionChecking ? Visibility.Collapsed : Visibility.Visible;
        SetInputsEnabled(false);
        LoginButton.IsEnabled = false;
        try { await action(); }
        catch (BridgeException e) when (e.Code == "auth")
        {
            try { await backend.CallAsync("Logout"); } catch { }
            ResetSession();
            ShowMessage(e.Message);
        }
        catch (Exception e) { ShowMessage(e.Message); if (!backend.Running) Restart.Visibility = Visibility.Visible; }
        finally { busy = false; App.Session.MainBusy = false; App.Session.Notify(); Progress.Visibility = Visibility.Collapsed; SetInputsEnabled(true); LoginButton.IsEnabled = true; UpdateLoginPresentation(); }
    }
    private void SetInputsEnabled(bool enabled)
    {
        (specializedPanel as SpeakerPanel)?.UpdateControls();
        EditModeButton.IsEnabled = enabled;
        adaptersPage.IsHitTestVisible=enabled;
        Rooms.IsEnabled = enabled;
        SearchHost.IsHitTestVisible = enabled;
        Search.IsEnabled = SearchButton.IsEnabled = enabled;
        AppearanceChoice.IsEnabled = enabled;
        CardSizeChoice.IsEnabled = enabled;
        DeviceList.IsEnabled = enabled;
        HomeOverviewHost.IsEnabled = enabled;
        Homes.IsEnabled = enabled;
        RefreshList.IsEnabled = enabled;
    }
    private async Task StartAsync()
    {
        sessionChecking = true;
        ShowCurrentView();
        try
        {
            await backend.StartAsync();
            Restart.Visibility = Visibility.Collapsed;
            try { authenticated = (await backend.CallAsync("GetSessionStatus")).GetProperty("authenticated").GetBoolean(); }
            catch (BridgeException e) when (e.Code == "network") { authenticated = true; ShowMessage(e.Message); }
            if (authenticated) await LoadHomesAsync();
        }
        finally { sessionChecking = false; ShowCurrentView(); }
    }
    private async Task LoadHomesAsync()
    {
        var selectedHome = (Homes.SelectedItem as Home)?.Id;
        var homes = (await backend.CallAsync("ListHomes")).Deserialize<List<Home>>(BackendClient.Json) ?? [];
        App.Session.SetHomes(homes);
        App.Automation.Bind((await backend.CallAsync("GetAutomationAccount")).GetProperty("account").GetString());
        Homes.ItemsSource = homes;
        Homes.SelectedItem = homes.FirstOrDefault(h => h.Id == selectedHome) ?? homes.FirstOrDefault();
        if (homes.Count > 0) await LoadDevicesAsync();
        else { browser.SetDevices([]); ApplyFilter(); ShowMessage("此账号没有可用家庭。请在手机米家中确认账号和地区。"); }
    }
    private async Task LoadDevicesAsync()
    {
        if (Homes.SelectedItem is not Home home) return;
        await App.Session.SetHomeAsync(home, (Homes.ItemsSource as List<Home>) ?? []);
        Back();
        homeLightingFilter=false;
        homePreferences=(await backend.CallAsync("GetHomePreferences",new{homeId=home.Id})).Deserialize<HomePreferences>(BackendClient.Json)??new([],[]);
        var selectedRoom = (Rooms.SelectedItem?.Tag as Room)?.Id;
        var rooms = new[] { new Room("*", "全部") }.Concat(home.Rooms).Append(new Room("", "未分配房间")).ToList();
        changingRooms = true;
        Rooms.Items.Clear();
        foreach (var room in rooms) Rooms.Items.Add(new SelectorBarItem { Text = room.Name, Tag = room });
        Rooms.SelectedItem = Rooms.Items.FirstOrDefault(r => (r.Tag as Room)?.Id == selectedRoom) ?? Rooms.Items[0];
        changingRooms = false;
        browser.SetOrder((await backend.CallAsync("GetCardOrder", new { homeId = home.Id })).Deserialize<List<string>>(BackendClient.Json) ?? []);
        var parameters = new { homeId = home.Id, rooms = home.Rooms.ToDictionary(r => r.Id, r => r.Name), cached = false };
        var cached = (await backend.CallAsync("ListDevices", new { parameters.homeId, parameters.rooms, cached = true })).Deserialize<List<DeviceIdentity>>(BackendClient.Json);
        browser.SetDevices(cached ?? []);
        ApplyFilter();
        browser.SetDevices((await backend.CallAsync("ListDevices", parameters)).Deserialize<List<DeviceIdentity>>(BackendClient.Json) ?? []);
        ApplyFilter();
        Notice.IsOpen = false;
        Notice.Visibility = Visibility.Collapsed;
        var ids = browser.AllDevices.Where(d => d.Online && DeviceCardPresentation.CanReadCard(d)).Select(d => d.Id).ToList();
        if (ids.Count > 0)
        {
            try
            {
                var states = new List<DeviceCardState>();
                foreach (var batch in ids.Chunk(200))
                    states.AddRange((await backend.CallAsync("ReadCardStates", new { deviceIds = batch })).Deserialize<List<DeviceCardState>>(BackendClient.Json) ?? []);
                browser.SetCardStates(states);
            }
            catch (BridgeException e) when (e.Code == "auth") { throw; }
            catch { ShowMessage("部分设备状态未更新，请重试。"); }
            ApplyFilter();
        }
    }
    private void ApplyFilter()
    {
        var overview = Navigation.SelectedItem is NavigationViewItem item && item.Tag?.ToString() == "overview";
        var search = overview ? "" : Search.Text.Trim();
        var room = overview ? "*" : (Rooms.SelectedItem?.Tag as Room)?.Id;
        browser.Filter(search, room, overview||!homeLightingFilter?null:"light");
        UpdateHome();
        App.Session.Notify();
        Empty.Visibility = browser.Devices.Count == 0 && !detailOpen ? Visibility.Visible : Visibility.Collapsed;
        if (detailOpen && !browser.IsEditing) DeviceList.SelectedItem = browser.Tiles.FirstOrDefault(t => t.Device.Id == selectedDevice?.Id);
        ShowCurrentView();
    }
    private void ShowCurrentView()
    {
        if (!ready) return;
        var page = (Navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        var overview = page == "overview";
        var automation = page == "automation";
        var showWorkspace = authenticated && !sessionChecking;
        Navigation.IsPaneVisible = showWorkspace;
        Navigation.IsSettingsVisible = showWorkspace;
        Navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        Navigation.IsBackEnabled = detailOpen;
        ContentShell.BorderThickness = showWorkspace ? new Thickness(1, 1, 0, 0) : new Thickness(0);
        ContentShell.CornerRadius = showWorkspace ? new CornerRadius(8, 0, 0, 0) : new CornerRadius(0);
        HeaderLayout.Visibility = showWorkspace && !detailFull ? Visibility.Visible : Visibility.Collapsed;
        RestoreView.Visibility = sessionChecking ? Visibility.Visible : Visibility.Collapsed;
        RestoreProgress.IsActive = sessionChecking;
        Progress.Visibility = busy && !sessionChecking ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = settingsSelected && showWorkspace && !licensesOpen ? Visibility.Visible : Visibility.Collapsed;
        LicensesPage.Visibility = settingsSelected && showWorkspace && licensesOpen ? Visibility.Visible : Visibility.Collapsed;
        LoginView.Visibility = !authenticated && !sessionChecking ? Visibility.Visible : Visibility.Collapsed;
        OverviewView.Visibility = !settingsSelected && showWorkspace && overview ? Visibility.Visible : Visibility.Collapsed;
        DevicesView.Visibility = !settingsSelected && showWorkspace && !overview && !automation ? Visibility.Visible : Visibility.Collapsed;
        var showAutomation = !settingsSelected && showWorkspace && automation;
        AutomationView.Visibility = showAutomation ? Visibility.Visible : Visibility.Collapsed;
        if (showAutomation) AutomationView.Open();
        HeaderActions.Visibility = showWorkspace && !settingsSelected && !detailFull && !automation ? Visibility.Visible : Visibility.Collapsed;
        EditModeButton.Visibility=overview?Visibility.Collapsed:Visibility.Visible;
        Filters.Visibility = showWorkspace && !settingsSelected && !overview && !detailFull && !automation ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = UiText.Get(settingsSelected ? licensesOpen ? "LicenseTitle" : "SettingsTitle" : automation ? "AutoTitle" : overview ? "HomeTitle" : "DevicesTitle");
        var offline = browser.Devices.Count(d => !d.Online);
        HeaderLayout.MaxWidth = overview && !settingsSelected ? 1320 : double.PositiveInfinity;
        PageDescription.Text = settingsSelected || automation ? "" : overview ? homeView?.Headline ?? "" : UiText.Format("DeviceCount",browser.Devices.Count)+(offline>0?" · "+UiText.Format("OfflineCount",offline):"");
        PageDescription.Visibility = string.IsNullOrEmpty(PageDescription.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdateLoginPresentation();
        DispatcherQueue.TryEnqueue(UpdateResponsiveLayout);
    }
    private void ShowMessage(string message, InfoBarSeverity severity = InfoBarSeverity.Warning)
    { Notice.Message = UiText.State(message); Notice.Severity = severity; Notice.IsOpen = true; Notice.Visibility = Visibility.Visible; }
    private void OnOpenLicenses(object sender, RoutedEventArgs e) => SelectSettings("licenses");
    private void CloseLicenses() => SelectSettings("licenses");
    private void OnNavigation(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (App.StartupDiagnostic) App.RecordStartupStage("Navigation changed: " + (args.SelectedItem as NavigationViewItem)?.Tag);
        viewGeneration++;
        settingsSelected = args.IsSettingsSelected;
        licensesOpen = false;
        homeLightingFilter=false;
        browser.IsEditing = false;
        EditModeButton.IsChecked = false; EditModeButton.Content = MiHome.WinUI.Infrastructure.UiText.Get("S051836569928");
        DeviceList.CanDragItems = DeviceList.AllowDrop = false;
        if (ready && detailOpen) Back();
        else if (ready) ApplyFilter();
        else ShowCurrentView();
    }
    private void OnNavigationBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) { if (licensesOpen) CloseLicenses(); else if (detailOpen) Back(); }
    private void OnShowDevices(object sender, RoutedEventArgs e) => Navigation.SelectedItem = Navigation.MenuItems[1];
    private async void OnLogin(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await backend.CallAsync("CancelLogin");
        QrImage.Source = null;
        SetLoginStatus(UiText.Get("GettingQr"));
        loginSession = (await backend.CallAsync("BeginLogin")).GetProperty("sessionId").GetInt64();
        UpdateLoginPresentation();
    });
    private async void OnCancelLogin(object sender, RoutedEventArgs e)
    {
        loginSession = 0;
        QrImage.Source = null;
        SetLoginStatus(null);
        UpdateLoginPresentation();
        try { await backend.CallAsync("CancelLogin"); } catch (Exception ex) { ShowMessage(ex.Message); }
    }
    private async void HandleEvent(JsonElement message)
    {
        if (closing || message.GetProperty("event").GetString() != "Login" || message.GetProperty("sessionId").GetInt64() != loginSession) return;
        var data = message.GetProperty("data");
        switch (data.GetProperty("status").GetString())
        {
            case "waiting":
                try
                {
                    using var generator = new QRCodeGenerator();
                    using var qr = generator.CreateQrCode(data.GetProperty("qr").GetString()!, QRCodeGenerator.ECCLevel.M);
                    using var code = new PngByteQRCode(qr);
                    var bytes = code.GetGraphic(8);
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (message.GetProperty("sessionId").GetInt64() == loginSession) { QrImage.Source = bitmap; SetLoginStatus("请在米家 App 中确认登录。"); UpdateLoginPresentation(); }
                }
                catch { SetLoginStatus("二维码显示失败，请重新获取。"); }
                break;
            case "authenticated":
                authenticated = true;
                QrImage.Source = null;
                loginSession = 0;
                SetLoginStatus(null);
                ShowCurrentView();
                await RunAsync(LoadHomesAsync);
                break;
            case "failed":
                QrImage.Source = null;
                loginSession = 0;
                SetLoginStatus(data.GetProperty("message").GetString());
                UpdateLoginPresentation();
                break;
        }
    }
    private async void OnHomeChanged(object sender, SelectionChangedEventArgs e)
    { if (ready && !busy && authenticated) await RunAsync(LoadDevicesAsync); }
    private void OnRoomChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e) { if (ready && !changingRooms) {homeLightingFilter=false;ApplyFilter();} }
    private void OnDetailBack(object sender, RoutedEventArgs e) => Back();
    private void OnExpandSearch(object sender, RoutedEventArgs e)
    { SearchButton.Visibility = Visibility.Collapsed; Search.Visibility = Visibility.Visible; Search.Focus(FocusState.Programmatic); UpdateResponsiveLayout(); }
    private void CollapseEmptySearch()
    { if (Search.Text.Length == 0) { Search.Visibility = Visibility.Collapsed; SearchButton.Visibility = Visibility.Visible; UpdateResponsiveLayout(); } }
    private void OnSearchLostFocus(object sender, RoutedEventArgs e)
    { if (Search.Visibility == Visibility.Visible && Search.FocusState == FocusState.Unfocused) CollapseEmptySearch(); }
    private void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        if (Search.Text.Length > 0) Search.Text = "";
        else { CollapseEmptySearch(); SearchButton.Focus(FocusState.Programmatic); }
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e) { if (ready) ApplyFilter(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) {if(previewRefresh is not null){await previewRefresh();return;}await RunAsync(LoadHomesAsync);}
    private async void OnDeviceClick(object sender, ItemClickEventArgs e)
    {
        if(sender is DeviceGridView grid && grid.ConsumeControlInteraction())return;
        if (browser.IsEditing) return;
        await RunAsync(() => OpenDeviceAsync(e.ClickedItem is DeviceTile tile ? tile.Device : (DeviceIdentity)e.ClickedItem));
    }
    private async void OnParentSwitchSettings(object sender, RoutedEventArgs e)
    {
        if (selectedDevice is { } selected && DeviceListing.Parent(selected, browser.AllDevices) is { } parent)
            await RunAsync(() => OpenDeviceAsync(parent));
    }
    private async Task OpenDeviceAsync(DeviceIdentity selected, bool stayOnHome = false)
    {
        var generation = ++viewGeneration;
        var schema = await (diagnosticService ?? backend).GetSchemaAsync(selected.Id);
        if (generation != viewGeneration) return;
        if (!stayOnHome && Navigation.SelectedItem != Navigation.MenuItems[1]) Navigation.SelectedItem = Navigation.MenuItems[1];
        selectedDevice = selected;
        ParentSwitchSettings.Visibility = DeviceListing.Parent(selected, browser.AllDevices) is null ? Visibility.Collapsed : Visibility.Visible;
        DeviceReadingsSeparator.Visibility = DeviceConsumablesItem.Visibility = diagnosticService is null ? Visibility.Visible : Visibility.Collapsed;
        DeviceEnergyItem.Visibility = diagnosticService is null && selected.SupportsEnergyHistory ? Visibility.Visible : Visibility.Collapsed;
        settingCardAppearance = true;
        CardSizeChoice.ItemsSource = DeviceExperience.Sizes(selected);
        CardSizeChoice.SelectedItem = ((CardAppearanceOption[])CardSizeChoice.ItemsSource).FirstOrDefault(s => s.Id == selected.CardSize)
            ?? ((CardAppearanceOption[])CardSizeChoice.ItemsSource)[0];
        AppearanceChoice.ItemsSource = DeviceCardPresentation.Appearances;
        AppearanceChoice.SelectedItem = DeviceCardPresentation.Appearances.FirstOrDefault(a => a.Id == selected.CardAppearance)
            ?? DeviceCardPresentation.Appearances[0];
        CardAppearance.Visibility = Visibility.Collapsed;
        AppearanceChoice.Visibility = DeviceCardPresentation.CanChooseAppearance(selected) ? Visibility.Visible : Visibility.Collapsed;
        CardAppearance.IsExpanded = false;
        settingCardAppearance = false;
        DetailTitle.Text = selected.Name;
        DetailIcon.Content = DeviceUi.DeviceIcon(new DeviceTile(selected), DetailIcon,36);
        DetailStatus.Text = selected.Room+" · "+UiText.Get(selected.Online?"Online":"OfflineCount").Replace("{0} ","");
        InformationRoom.Text = selected.Room;
        InformationModel.Text = selected.Model;
        DeviceInformation.IsExpanded = false;
        DeviceInformation.Visibility = Visibility.Collapsed;
        var context = new PanelContext(selected, schema, diagnosticService ?? backend);
        var panel = selected.Adapter is not null ? (FrameworkElement)new AdapterPanel(selected,schema,context.Service)
            : SpeakerPanel.Supports(context) ? new SpeakerPanel(context, browser.HomeTiles.FirstOrDefault(tile => tile.Device.Id == selected.Id)?.State,
                () => detailOpen && !closing && !busy && generation == viewGeneration)
            : panels.Create(context, generic);
        if(selected.AdapterIssue is {} issue)ShowMessage(issue);
        if (panel is AdapterPanel)
        {
            genericPanel=null;PrimaryControls.Content=null;PrimaryControls.Visibility=Visibility.Collapsed;
            SpecializedContent.Content=panel;SpecializedContent.Visibility=Visibility.Visible;
            GenericContent.Content=null;GenericExpander.Visibility=Visibility.Collapsed;
        }
        else if (panel is GenericPanel gp)
        {
            genericPanel = gp;
            PrimaryControls.Content = gp;
            PrimaryControls.Visibility = Visibility.Visible;
            SpecializedContent.Content = null;
            SpecializedContent.Visibility = Visibility.Collapsed;
            GenericContent.Content = null;
            GenericExpander.Visibility = Visibility.Collapsed;
        }
        else if (panel is DeviceExperiencePanel experience)
        {
            genericPanel = null;
            PrimaryControls.Content = null;
            PrimaryControls.Visibility = Visibility.Collapsed;
            SpecializedContent.Content = experience;
            SpecializedContent.Visibility = Visibility.Visible;
            GenericContent.Content = null;
            GenericExpander.Visibility = Visibility.Collapsed;
            experience.CardStateRead += OnDetailCardStateRead;
        }
        else
        {
            genericPanel = new GenericPanel(context);
            PrimaryControls.Content = null;
            PrimaryControls.Visibility = Visibility.Collapsed;
            SpecializedContent.Content = panel;
            SpecializedContent.Visibility = Visibility.Visible;
            GenericContent.Content = genericPanel;
            GenericExpander.Visibility = Visibility.Visible;
            GenericExpander.IsExpanded = false;
        }
        if(panel is AdapterPanel adapterPanel) adapterPanel.CardStateRead+=OnDetailCardStateRead;
        if (panel is SpeakerPanel speakerPanel)
        {
            speakerPanel.StateRead += OnDetailCardStateRead;
            speakerPanel.AuthenticationLost += () => { if (generation == viewGeneration) OnSharedAuthenticationLost(); };
        }
        if (genericPanel is not null && DeviceCardPresentation.CanReadCard(selected))
            genericPanel.CardStateRead += OnDetailCardStateRead;
        specializedPanel = panel is GenericPanel ? null : panel as IRefreshablePanel;
        detailOpen = true;
        detailFull = false;
        ApplyDetailPresentation();
        DetailView.Visibility = Visibility.Visible;
        DeviceList.Visibility = Visibility.Visible;
        Empty.Visibility = Visibility.Collapsed;
        ShowCurrentView();
        DeviceDrawer.UpdateLayout();
        ShowDrawerAnimated();
        FocusDrawer();
        if (preferences.RefreshSeconds > 0) timer.Start();
        _ = RefreshDetailAsync();
    }
    private void OnShowCardSettings(object sender, RoutedEventArgs args)
    { detailFull = true; ApplyDetailPresentation(); ShowCurrentView(); CardAppearance.Visibility = Visibility.Visible; CardAppearance.IsExpanded = true; CardAppearance.StartBringIntoView(); }
    private void OnShowDeviceInformation(object sender, RoutedEventArgs args)
    { detailFull = true; ApplyDetailPresentation(); ShowCurrentView(); DeviceInformation.Visibility = Visibility.Visible; DeviceInformation.IsExpanded = true; DeviceInformation.StartBringIntoView(); }
    private async void OnCardWriteRequested(object? sender, CardWriteRequestedEventArgs args)
    {
        if (busy || !authenticated || args.Tile.State is not { } state) return;
        await RunAsync(async () =>
        {
            var session = new DeviceControlSession(args.Tile.Device, state.Schema, diagnosticService??backend);
            session.StateRead += snapshot => { browser.UpdateCardState(snapshot); ApplyFilter(); };
            await session.WriteAsync(args.Property, args.Value);
            if (session.Feedback is { } message) ShowMessage(message);
        });
    }
    private void OnCardLoaded(object sender,RoutedEventArgs args)
    {
        if(sender is DeviceCardView card)card.Configure(diagnosticService??backend,snapshot=>{browser.UpdateCardState(snapshot);ApplyFilter();},message=>ShowMessage(message));
    }
    private async void OnCardSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settingCardAppearance || selectedDevice is null || CardSizeChoice.SelectedItem is not CardAppearanceOption choice) return;
        var deviceId = selectedDevice.Id;
        await RunAsync(() => SetCardSizeAsync(deviceId, choice.Id));
        if (selectedDevice?.Id == deviceId)
        {
            settingCardAppearance = true;
            CardSizeChoice.SelectedItem = ((CardAppearanceOption[])CardSizeChoice.ItemsSource).FirstOrDefault(s => s.Id == selectedDevice.CardSize)
                ?? ((CardAppearanceOption[])CardSizeChoice.ItemsSource)[0];
            settingCardAppearance = false;
        }
    }
    private async Task SetCardSizeAsync(string deviceId, string size)
    {
        var updated = (await backend.CallAsync("SetCardSize", new { deviceId, size })).Deserialize<DeviceIdentity>(BackendClient.Json)!;
        browser.ReplaceDevice(updated);
        if (selectedDevice?.Id == deviceId) selectedDevice = updated;
        ApplyFilter();
    }
    private async void OnCardResizeRequested(object? sender, CardResizeRequestedEventArgs args)
    {
        if (busy || !authenticated || !browser.IsEditing) return;
        await RunAsync(() => SetCardSizeAsync(args.Tile.Device.Id, args.Size));
        DispatcherQueue.TryEnqueue(() =>
        {
            var view = DeviceList;
            view.UpdateLayout();
            for (var i = 0; i < view.Items.Count; i++)
                if (view.Items[i] is DeviceTile tile && tile.Device.Id == args.Tile.Device.Id &&
                    view.ContainerFromIndex(i) is GridViewItem container && container.ContentTemplateRoot is DeviceCardView card)
                { card.FocusResizeHandle(); break; }
        });
    }
    private void OnDetailCardStateRead(DeviceCardState state)
    {
        if (!detailOpen || selectedDevice?.Id != state.DeviceId || closing) return;
        browser.UpdateCardState(state);
        UpdateHome();
        DetailIcon.Content = DeviceUi.DeviceIcon(new DeviceTile(selectedDevice, state), DetailIcon,36);
        DetailStatus.Text = selectedDevice.Room + " · " + (selectedDevice.Online?UiText.Get("Online"):UiText.State("离线或状态未知"));
        ToolTipService.SetToolTip(DetailStatus, state.ReadAt is { } time ? $"云端读取于 {time.ToLocalTime():HH:mm}，不代表设备测量时间。" : "读取失败，请刷新。");
        // Rebuilding the selected GridView here can dismiss an overlay pane.
        // Keep the detail controls and focus intact; Back projects the cached state.
    }
    private async void OnCardAppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settingCardAppearance || selectedDevice is null || AppearanceChoice.SelectedItem is not CardAppearanceOption choice) return;
        var deviceId = selectedDevice.Id;
        await RunAsync(async () =>
        {
            var updated = (await backend.CallAsync("SetCardAppearance", new { deviceId, category = choice.Id })).Deserialize<DeviceIdentity>(BackendClient.Json)!;
            browser.ReplaceDevice(updated);
            if (selectedDevice?.Id == deviceId) selectedDevice = updated;
            ApplyFilter();
        });
        if (selectedDevice?.Id == deviceId)
        {
            settingCardAppearance = true;
            AppearanceChoice.SelectedItem = DeviceCardPresentation.Appearances.FirstOrDefault(a => a.Id == selectedDevice.CardAppearance)
                ?? DeviceCardPresentation.Appearances[0];
            settingCardAppearance = false;
        }
    }
    private async Task RefreshDetailAsync()
    {
        if (!detailOpen || closing || refreshingDetailGeneration == viewGeneration) return;
        var generation = viewGeneration;
        var primary = genericPanel;
        var specialized = specializedPanel;
        refreshingDetailGeneration = generation;
        try
        {
            if (primary is not null) await primary.RefreshAsync();
            if (specialized is not null) await specialized.RefreshAsync();
        }
        catch (BridgeException e) when (e.Code == "auth")
        {
            if (generation != viewGeneration || closing) return;
            try { await backend.CallAsync("Logout"); } catch { }
            ResetSession(); ShowMessage(e.Message);
        }
        catch (Exception e)
        {
            if (generation == viewGeneration && !closing) ShowMessage(e.Message);
        }
        finally
        {
            if (refreshingDetailGeneration == generation) refreshingDetailGeneration = -1;
        }
    }
    private async void OnRefreshDetail(object sender, RoutedEventArgs e) => await RefreshDetailAsync();
    private void Back([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        viewGeneration++;
        timer.Stop();
        if (App.StartupDiagnostic && detailOpen) App.RecordStartupStage("Drawer close requested by " + caller);
        var previousDevice = selectedDevice?.Id;
        detailOpen = false;
        detailFull = false;
        selectedDevice = null;
        (specializedPanel as SpeakerPanel)?.ClearDraft();
        HideDrawerAnimated();
        DeviceList.Visibility = Visibility.Visible;
        ApplyFilter();
        if (previousDevice is not null && DevicesView.Visibility==Visibility.Visible) FocusCard(previousDevice);
    }
    private void OnBack(object sender, RoutedEventArgs e) => Back();
    private void ResetSession()
    {
        App.Automation.Bind(null);
        App.Session.Clear();
        authenticated = false;
        settingsSelected = false;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        loginSession = 0;
        browser.SetDevices([]);
        Homes.ItemsSource = null;
        changingRooms = true;
        Rooms.Items.Clear();
        changingRooms = false;
        QrImage.Source = null;
        Back();
        SetLoginStatus(null);
        ShowCurrentView();
    }
    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog=new Dialogs.MessageDialog { XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme,Title="退出登录？",PrimaryButtonText=MiHome.WinUI.Infrastructure.UiText.Get("S3ab8cc15939f") };
        dialog.SetBody(DocumentPresenter.Plain("将清除本机凭据和账号缓存，下次需要重新登录。"),300);
        var result=await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        await RunAsync(async () => { await backend.CallAsync("Logout"); ResetSession(); ShowMessage("已退出登录。", InfoBarSeverity.Informational); });
    }
    private async void OnRestart(object sender, RoutedEventArgs e) => await RunAsync(async () => { ResetSession(); await StartAsync(); });
    private void LoadTheme()
    {
        loadingPreferences = true;
        preferences = AppPreferences.Load(DataDirectory);
        appearancePage.SelectedLanguage=preferences.Language;aboutPage.CheckAutomatically=preferences.CheckUpdates;
        appearancePage.ThemeIndex = preferences.Theme;
        appearancePage.RefreshIndex = preferences.RefreshSeconds switch { 10 => 0, 60 => 2, 0 => 3, _ => 1 };
        loadingPreferences = false;
        if (!App.StartupDiagnostic) LoadDesktopPreferences();
        ApplyPreferences();
    }
    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || loadingPreferences) return;
        preferences = preferences with { Theme = Math.Max(0, appearancePage.ThemeIndex),
            RefreshSeconds = appearancePage.RefreshIndex switch { 0 => 10, 2 => 60, 3 => 0, _ => 30 } };
        ApplyPreferences();
        try { if (!App.StartupDiagnostic) preferences.Save(DataDirectory); }
        catch { ShowMessage(MiHome.WinUI.Infrastructure.UiText.Get("S41d27d68e150")); }
    }
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    private void OnGridLoaded(object sender, RoutedEventArgs e) => UpdateResponsiveLayout();
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    private void UpdateResponsiveLayout()
    {
        if (!ready) return;
        var width = Math.Max(1, ContentArea.ActualWidth - ContentArea.Padding.Left - ContentArea.Padding.Right);
        SettingsView.Width = Math.Min(1000, Math.Max(1,width-64));
        var pageWidth = Math.Max(1,width-DrawerColumn.ActualWidth-64);
        var compactHeader = pageWidth < 680;
        Grid.SetColumn(HeaderActions, compactHeader ? 0 : 1);
        Grid.SetRow(HeaderActions, compactHeader ? 1 : 0);
        Grid.SetColumnSpan(HeaderActions, compactHeader ? 2 : 1);
        HeaderActions.Margin = compactHeader ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        HeaderActions.HorizontalAlignment = compactHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var narrowSearch = Search.Visibility == Visibility.Visible && pageWidth < 640;
        Grid.SetRow(SearchHost, narrowSearch ? 1 : 0);
        Grid.SetColumn(SearchHost, narrowSearch ? 0 : 1);
        Grid.SetColumnSpan(SearchHost, narrowSearch ? 2 : 1);
        Grid.SetColumnSpan(RoomScroll, narrowSearch ? 2 : 1);
        SearchHost.HorizontalAlignment = HorizontalAlignment.Right;
        Search.Width = Math.Max(120, Math.Min(240, pageWidth));
        ApplyDetailPresentation();
        ConfigureCards(DeviceList);

    }
    private void SetLoginStatus(string? message)
    {
        LoginStatus.Text = UiText.State(message??"");
        LoginStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateLoginPresentation()
    {
        QrPanel.Visibility = QrImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        CancelLoginButton.Visibility = loginSession == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoginButton.Content = UiText.Get(loginSession==0?"SignIn":"NewQr");
    }
    private void OnSystemThemeChanged(Microsoft.UI.System.ThemeSettings sender, object args) => DispatcherQueue.TryEnqueue(() => { UpdateTitleBarTheme(); });
    private void UpdateTitleBarTheme()
    {
        AppWindow.TitleBar.PreferredTheme = themeSettings.HighContrast ? TitleBarTheme.UseDefaultAppMode :
            Root.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        AppWindow.TitleBar.ButtonBackgroundColor = themeSettings.HighContrast ? null : Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = themeSettings.HighContrast ? null : Microsoft.UI.Colors.Transparent;
    }
    private static void ConfigureCards(GridView view)
    {
        if (view.ItemsPanelRoot is VariableSizedWrapGrid panel && view.ActualWidth > 0)
        {
            var available = Math.Max(1, view.ActualWidth - view.Padding.Left - view.Padding.Right - 4);
            var columns = Math.Max(1, (int)(available / 240));
            panel.MaximumRowsOrColumns = columns;
            panel.ItemWidth = available / columns;
        }
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
}
