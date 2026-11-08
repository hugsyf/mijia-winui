using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;
using MiHome.WinUI.ViewModels;
using Windows.Foundation;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private IDeviceService? diagnosticService;
    private DispatcherTimer? adapterPreviewTimer;
    private Func<Task>? previewRefresh;
    private Func<string,object?,Task<JsonElement>>? adapterPreviewRpc;
    private List<string>? adapterPreviewImports;
    private Task<JsonElement> AdapterCallAsync(string method,object? parameters=null)=>adapterPreviewRpc?.Invoke(method,parameters)??backend.CallAsync(method,parameters);
    internal async Task PrepareAdapterManagerPreviewAsync(string fixturePath)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(fixturePath));
        var catalog=fixture.RootElement.GetProperty("catalog").Deserialize<List<AdapterPackage>>(BackendClient.Json)!.ToDictionary(p=>p.Id);
        var inspections=fixture.RootElement.GetProperty("imports").EnumerateArray().ToDictionary(i=>i.GetProperty("path").GetString()!,i=>i.GetProperty("inspection").Clone());
        adapterPreviewImports=inspections.Keys.ToList();
        adapterPreviewRpc=(method,args)=>
        {
            var parameters=JsonSerializer.SerializeToElement(args,BackendClient.Json);
            JsonElement result=JsonSerializer.SerializeToElement(new{});
            switch(method)
            {
                case "ListAdapters":result=JsonSerializer.SerializeToElement(catalog.Values.ToList(),BackendClient.Json);break;
                case "InspectAdapter":result=inspections[parameters.GetProperty("path").GetString()!];break;
                case "EnableAdapter":
                    var enabledId=parameters.GetProperty("id").GetString()!;catalog[enabledId]=catalog[enabledId] with{Enabled=parameters.GetProperty("enabled").GetBoolean()};break;
                case "RemoveAdapter":catalog.Remove(parameters.GetProperty("id").GetString()!);break;
                case "InstallAdapter":
                    var inspected=inspections[parameters.GetProperty("path").GetString()!];var definition=inspected.GetProperty("definition").Deserialize<DeviceAdapter>(BackendClient.Json)!;
                    catalog[definition.Id]=new(definition.Id,definition.Name,definition.Version,inspected.GetProperty("document").GetProperty("models").Deserialize<List<string>>()!,true,false){SdkVersion="0.17.0"};break;
                default:throw new InvalidOperationException("此模拟窗口不连接设备。");
            }
            return Task.FromResult(result);
        };
        ready=true;authenticated=true;sessionChecking=false;timer.Stop();
        preferences=preferences with{RefreshSeconds=0};
        ShowCurrentView();Navigation.SelectedItem=Navigation.SettingsItem;settingsSelected=true;SelectSettings("adapters");await RefreshAdapterCatalogAsync();
        Title="米家 · 适配管理验收 · 模拟数据";
    }
    internal async Task PrepareAdapterPreviewAsync(string previewPath,string definitionPath)
    {
        using var preview=JsonDocument.Parse(File.ReadAllText(previewPath));
        var schema=preview.RootElement.GetProperty("schema").Deserialize<DeviceSchema>(BackendClient.Json)!;
        var adapter=JsonSerializer.Deserialize<DeviceAdapter>(File.ReadAllText(definitionPath),BackendClient.Json)!;
        var service=new AdapterPreviewService(schema,0,adapter);diagnosticService=service;
        ready=true;authenticated=true;sessionChecking=false;
        busy=true;Homes.ItemsSource=new[]{new Home("preview","模拟家庭",[new Room("","模拟房间")])};Homes.SelectedIndex=0;busy=false;
        changingRooms=true;Rooms.Items.Clear();Rooms.Items.Add(new SelectorBarItem{Text=MiHome.WinUI.Infrastructure.UiText.Get("S5c55a67935af"),Tag=new Room("*","全部")});Rooms.SelectedItem=Rooms.Items[0];changingRooms=false;
        preferences=preferences with{RefreshSeconds=0};timer.Stop();
        var device=new DeviceIdentity("preview-adapter",adapter.Name,preview.RootElement.GetProperty("model").GetString()!,preview.RootElement.GetProperty("specType").GetString()!,"preview","模拟房间",true){Adapter=adapter,CardSize="expanded"};
        browser.SetDevices([device]);
        async Task Refresh()
        {
            browser.UpdateCardState(new(device.Id,schema,await service.ReadAsync(device.Id),DateTimeOffset.UtcNow,null){Adapter=adapter});
            ApplyFilter();
        }
        previewRefresh=Refresh;Navigation.SelectedItem=Navigation.MenuItems[1];await Refresh();
        adapterPreviewTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        adapterPreviewTimer.Tick+=async(_,_)=>await Refresh();adapterPreviewTimer.Start();
        Closed+=(_,_)=>adapterPreviewTimer.Stop();
        Title="米家 · "+adapter.Name+" · 模拟设备";
    }
    // Interactive visual review: isolated mock data, no backend connection or writes.
    internal Task PrepareWorkspacePreviewAsync(string fixturePath)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var fixture = data.RootElement.GetProperty("miaomiaoce.sensor_ht.t9");
        var schema = fixture.Deserialize<DeviceSchema>(BackendClient.Json)!;
        var service = new LayoutDiagnosticService { Schema = schema };
        service.Values = JsonSerializer.SerializeToElement(schema.Properties.Select(p => new { siid=p.Siid, piid=p.Piid, code=0,
            value=p.Type=="bool"?(object)false:p.Values.Count>0?p.Values[0].Value:p.Range.Length>0?Math.Clamp(24d,p.Range[0],p.Range[1]):0d }));
        diagnosticService = service;
        ready = true; authenticated = true; sessionChecking = false;
        preferences = preferences with { DrawerMode = "auto", RefreshSeconds = 0 };
        loadingPreferences = true;
        appearancePage.ThemeIndex = 1;
        appearancePage.RefreshIndex = 3;
        loadingPreferences = false;
        Root.RequestedTheme = ElementTheme.Light;
        var devices = Enumerable.Range(0,8).Select(i => new DeviceIdentity("preview-"+i,
            i == 0 ? "米家智能温湿度计 3" : "温湿度计 · " + (i+1), "miaomiaoce.sensor_ht.t9", fixture.GetProperty("specType").GetString()!, "preview", "示例房间", true)).ToList();
        browser.SetDevices(devices);
        browser.SetCardStates(devices.Select(d => new DeviceCardState(d.Id,schema,service.Values,DateTimeOffset.Now,null)).ToList());
        Navigation.SelectedItem = Navigation.MenuItems[1];
        ApplyFilter();
        Title = "米家 · 界面验收（模拟设备）";
        return Task.CompletedTask;
    }
    internal async Task ValidateDrawerFlowAsync(string fixturePath)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var fixture = data.RootElement.GetProperty("xiaomi.humidifier.600");
        var schema = fixture.Deserialize<DeviceSchema>(BackendClient.Json)!;
        var device = new DeviceIdentity("drawer-flow", "加湿器", "xiaomi.humidifier.600", fixture.GetProperty("specType").GetString()!, "layout", "客厅", true);
        var service = new LayoutDiagnosticService { Schema = schema };
        service.Values = JsonSerializer.SerializeToElement(schema.Properties.Select(p => new { siid=p.Siid, piid=p.Piid, code=0,
            value=p.Type=="bool"?(object)false:p.Values.Count>0?p.Values[0].Value:p.Range.Length>0?p.Range[0]:0d }));
        diagnosticService = service;
        ready = true; authenticated = true; sessionChecking = false;
        browser.SetDevices([device]);
        Navigation.SelectedItem = Navigation.MenuItems[1];
        void Layout(double width)
        {
            Root.Width = width; Root.Height = 800;
            Root.Measure(new Size(width,800)); Root.Arrange(new Rect(0,0,width,800)); Root.UpdateLayout();
        }
        try
        {
            foreach (var mode in new[] { "auto", "inline", "overlay" })
            {
                preferences = preferences with { DrawerMode = mode, RefreshSeconds = 0 };
                ApplyFilter(); Layout(1450);
                await RunAsync(() => OpenDeviceAsync(device));
                Layout(1450); await Task.Delay(200); Layout(1450);
                if (!detailOpen || DrawerPane.Visibility != Visibility.Visible) throw new InvalidOperationException("Drawer immediately closed after opening: " + mode);
                await RunAsync(RefreshDetailAsync);
                Layout(1450); await Task.Delay(200); Layout(1450);
                if (!detailOpen || DrawerPane.Visibility != Visibility.Visible) throw new InvalidOperationException("Drawer closed after state refresh: " + mode);
                OnExpandDetail(this,new()); Layout(1450); await Task.Delay(200); Layout(1450);
                if (!detailOpen || DrawerPane.Visibility != Visibility.Visible) throw new InvalidOperationException("Drawer closed while expanding: " + mode);
                OnExpandDetail(this,new()); Layout(760); await Task.Delay(200); Layout(760);
                if (!detailOpen || DrawerPane.Visibility != Visibility.Visible) throw new InvalidOperationException("Drawer closed after resize: " + mode);
                busy = true; OnDetailBack(this,new()); busy = false; Layout(760); await Task.Delay(200);
                if (detailOpen || DrawerPane.Visibility == Visibility.Visible) throw new InvalidOperationException("Explicit close did not close the drawer.");
            }
            if (service.Writes != 0) throw new InvalidOperationException("Drawer flow attempted a device write.");
            App.RecordStartupStage("PASS: drawer open, state refresh, expand/collapse, resize and explicit close in auto/inline/overlay; no backend or writes");
        }
        finally { timer.Stop(); diagnosticService = null; }
    }
    // A component check only: synthetic public schemas, no activation, backend or device writes.
    internal async Task ValidateDeviceLayoutsAsync(string fixturePath)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var original = Content;
        var samples = new List<DeviceTile>();
        var service = new LayoutDiagnosticService();
        try
        {
            await ValidateSettingsAndDrawerAsync(data.RootElement, service);
            var licenses = Controls.LicensesView.LoadCatalog();
            if (licenses.Count == 0 || licenses.Any(c => string.IsNullOrWhiteSpace(Controls.LicensesView.ReadTerms(c))))
                throw new InvalidDataException("A component license is missing.");
            var licensesView = new Controls.LicensesView();
            foreach (var width in new[] { 360d, 960d })
            {
                var host = new Grid { Width = width };
                host.Children.Add(licensesView); Content = host;
                host.Measure(new Size(width, 650)); host.Arrange(new Rect(0, 0, width, 650)); host.UpdateLayout();
                if (licensesView.ActualWidth > width + 1) throw new InvalidOperationException("License view exceeds available width.");
                licensesView.SelectForDiagnostic(licenses.Count - 1);
                host.UpdateLayout();
                if (licensesView.ActualWidth > width + 1) throw new InvalidOperationException("License detail exceeds available width.");
                host.Children.Clear();
            }
            App.RecordStartupStage($"Loaded {licenses.Count} offline licenses; list/detail page measured at 360/960 DIP");
            var images = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Devices"), "*.png");
            foreach (var path in images)
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) throw new InvalidOperationException("Invalid product image");
                stream.Seek(0);
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("WinUI could not decode product image");
            }
            App.RecordStartupStage($"Decoded {images.Length} bundled product images");
            using var capabilityData = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "capability-device-specs.json")));
            foreach (var item in data.RootElement.EnumerateObject().Concat(capabilityData.RootElement.EnumerateObject()))
            {
                var device = new DeviceIdentity("diagnostic-" + item.Name, "示例设备", item.Name,
                    item.Value.GetProperty("specType").GetString()!, "diagnostic", "示例房间", true);
                var schema = item.Value.Deserialize<DeviceSchema>(BackendClient.Json)!;
                var values = schema.Properties.Select(p => new { siid = p.Siid, piid = p.Piid, code = 0,
                    value = p.Type == "bool" ? (object)true : p.Values.Count > 0 ? p.Values[0].Value :
                        p.Type == "string" ? "" : p.Range.Length > 0 ? p.Range[0] : 0d }).ToList();
                service.Values = JsonSerializer.SerializeToElement(values);
                var state = new DeviceCardState(device.Id, schema, service.Values, DateTimeOffset.UtcNow, null);
                foreach (var size in DeviceExperience.Sizes(device).Where(s => s.Id.Length > 0))
                    samples.Add(new(device with { CardSize = size.Id }, state, IsEditing: size.Id == "expanded"));
                if (DeviceExperience.Kind(device, schema) == ExperienceKind.Generic) continue;
                App.RecordStartupStage("Measuring device page " + item.Name);
                var page = new DeviceExperiencePanel(new(device, schema, service));
                await page.RefreshAsync();
                foreach (var width in new[] { 360d, 960d })
                {
                    var host = new Grid { Width = width };
                    host.Children.Add(page); Content = host;
                    host.Measure(new Size(width, double.PositiveInfinity));
                    host.Arrange(new Rect(0, 0, width, Math.Max(1800, host.DesiredSize.Height)));
                    host.UpdateLayout();
                    if (page.ActualWidth > width + 1 || !double.IsFinite(page.DesiredSize.Height))
                        throw new InvalidOperationException("Device page exceeds available width: " + item.Name);
                    host.Children.Clear();
                }
            }
            App.RecordStartupStage("Measuring mixed-size device cards");
            var cards = new Controls.DeviceGridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true,
                ItemTemplate = (DataTemplate)Root.Resources["DeviceTemplate"], ItemContainerStyle = (Style)Root.Resources["DeviceContainerStyle"],
                ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><VariableSizedWrapGrid Orientation=\"Horizontal\" ItemWidth=\"260\" ItemHeight=\"88\" /></ItemsPanelTemplate>") };
            cards.ItemsSource = samples;
            var board = new Grid(); board.Resources["GridViewItemCornerRadius"] = new CornerRadius(8);
            board.Children.Add(cards); Content = board;
            foreach (var width in new[] { 360d, 960d })
            {
                board.Width = width;
                board.Measure(new Size(width, 8000)); board.Arrange(new Rect(0, 0, width, 8000)); board.UpdateLayout();
                ConfigureCards(cards); board.UpdateLayout();
                await Task.Yield();
                board.UpdateLayout();
                for (var i = 0; i < samples.Count; i++)
                {
                    if (cards.ContainerFromIndex(i) is not GridViewItem container ||
                        container.ContentTemplateRoot is not Controls.DeviceCardView view || view.Tile is null)
                        throw new InvalidOperationException("Card data context is unavailable: " + i);
                    // An unactivated window does not raise Loaded. Run the same content initialization explicitly.
                    view.EnsureContent();
                    var handles = view.ResizeHandleCount;
                    if (handles != (view.Tile.IsEditing ? 1 : 0)) throw new InvalidOperationException("Resize handle is not scoped to editing mode.");
                }
                board.UpdateLayout();
                for (var i = 0; i < samples.Count; i++)
                {
                    var container = (GridViewItem)cards.ContainerFromIndex(i);
                    var view = (Controls.DeviceCardView)container.ContentTemplateRoot;
                    if (container.ActualWidth <= 0 || container.ActualWidth > width + 1 ||
                        VariableSizedWrapGrid.GetRowSpan(container) != (DeviceExperience.Size(samples[i].Device) == "compact" ? 1 : 2))
                        throw new InvalidOperationException($"Mixed card container {i}: width={container.ActualWidth}, available={width}, rows={VariableSizedWrapGrid.GetRowSpan(container)}, size={DeviceExperience.Size(samples[i].Device)}");
                    if (view.Content is null || view.DesiredSize.Height > container.ActualHeight + 1)
                        throw new InvalidOperationException($"Device card exceeds its cell: {i}; desired={view.DesiredSize.Height}; slot={container.ActualHeight}");
                }
            }
            if (service.Writes != 0) throw new InvalidOperationException("Layout initialization attempted a device write.");
            App.RecordStartupStage($"Device pages and {samples.Count} mixed cards measured at 360/960 DIP; no writes");
        }
        finally { Content = original; }
    }
    private async Task ValidateSettingsAndDrawerAsync(JsonElement fixtures, LayoutDiagnosticService service)
    {
        settingsContent.Content = null;
        foreach (var page in settingsPages.Values)
        foreach (var width in new[] { 360d, 960d })
        {
            var host = new Grid { Width = width };
            host.Children.Add(page); Content = host;
            host.Measure(new Size(width, double.PositiveInfinity));
            host.Arrange(new Rect(0,0,width,Math.Max(800,host.DesiredSize.Height))); host.UpdateLayout();
            if (page.ActualWidth > width + 1) throw new InvalidOperationException("Settings page exceeds available width.");
            host.Children.Clear();
        }
        settingsContent.Content = settingsPages[settingsSection];
        var model = "xiaomi.humidifier.600";
        var fixture = fixtures.GetProperty(model);
        var schema = fixture.Deserialize<DeviceSchema>(BackendClient.Json)!;
        var device = new DeviceIdentity("layout", "米家无雾加湿器 3", model, fixture.GetProperty("specType").GetString()!, "layout", "客厅", true);
        service.Values = JsonSerializer.SerializeToElement(schema.Properties.Select(p => new { siid=p.Siid,piid=p.Piid,code=0,
            value=p.Type=="bool"?(object)false:p.Values.Count>0?p.Values[0].Value:p.Range.Length>0?p.Range[0]:0d }));
        var panel = new DeviceExperiencePanel(new(device,schema,service));
        await panel.RefreshAsync();
        SpecializedContent.Content = panel; SpecializedContent.Visibility = Visibility.Visible;
        DetailView.Visibility = Visibility.Visible;
        foreach (var width in new[] { 360d, 960d })
        foreach (var expanded in new[] { false, true })
        {
            panel.SetExpanded(expanded);
            DetailColumn.MaxWidth = double.PositiveInfinity;
            var paneWidth = DrawerLayout.Width(width,expanded);
            DrawerPane.Width = paneWidth;
            DrawerPane.Visibility = Visibility.Visible;
            ContentArea.Measure(new Size(width,800)); ContentArea.Arrange(new Rect(0,0,width,800)); ContentArea.UpdateLayout();
            await Task.Yield();
            if (DetailView.ActualWidth > paneWidth + 1) throw new InvalidOperationException("Drawer exceeds available width.");
        }
        DrawerPane.Visibility = Visibility.Collapsed;
        SpecializedContent.Content=null; SpecializedContent.Visibility=Visibility.Collapsed; DetailView.Visibility=Visibility.Collapsed;
        App.RecordStartupStage("Settings sections and drawer/full-page layouts measured at 360/960 DIP; no backend connection");
    }
    private sealed class LayoutDiagnosticService : IDeviceService
    {
        public DeviceSchema Schema { get; set; } = new([]);
        public JsonElement Values { get; set; }
        public int Writes { get; private set; }
        public Task<DeviceSchema> GetSchemaAsync(string deviceId) => Task.FromResult(Schema);
        public Task<JsonElement> ReadAsync(string deviceId) => Task.FromResult(Values.Clone());
        public Task WriteAsync(PropertyKey key, object? value) { Writes++; throw new InvalidOperationException("Diagnostic writes are forbidden."); }
    }
}
