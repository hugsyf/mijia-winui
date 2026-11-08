using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private readonly Pages.AdaptersPage adaptersPage = new();
    private ListView adapterPackages => adaptersPage.PackageList;
    private SelectorBar adapterFilter => adaptersPage.FilterBar;
    private AppBarToggleButton adapterSelect => adaptersPage.SelectionToggle;
    private TextBlock adapterCount => adaptersPage.CountLabel;
    private StackPanel adapterSelectionBar => adaptersPage.SelectionToolbar;
    private ToggleSwitch adapterSelectionEnabled => adaptersPage.EnableSelection;
    private Button adapterRemoveSelected => adaptersPage.RemoveSelectedButton;
    private Button adapterSelectAll => adaptersPage.SelectAllButton;
    private InfoBar adapterResult => adaptersPage.ResultBar;
    private readonly System.Collections.ObjectModel.ObservableCollection<Pages.AdapterPackageRow> adapterRows=[];
    private List<AdapterPackage> adapterCatalog = [];
    private bool renderingAdapters;
    private string adapterSort="name";
    private bool adapterSortDescending;
    private static Button AdapterIconButton(string glyph,string label) => new IconActionButton(glyph,label);
    private void InitializeAdapters()
    {
        adaptersPage.ImportButton.Click += async (_, _) => await RunAsync(ImportAdapterAsync);
        adaptersPage.ExportButton.Click += async (_, _) => await RunAsync(ExportDefaultAdaptersAsync);
        adaptersPage.RefreshButton.Click += async (_, _) => await RunAsync(RefreshAdapterCatalogAsync);
        adaptersPage.GuideButton.Click += (_, _) => OpenAdapterGuide();
        adaptersPage.SortRequested += (key, descending) => { adapterSort = key; adapterSortDescending = descending; RenderAdapterCatalog(); };
        adaptersPage.PackageCommand += async (package, command, enabled) =>
        {
            if (command == "info") { await ShowAdapterInformationAsync(package); return; }
            await RunAsync(async () =>
            {
                if (command == "remove") { await RemoveAdaptersAsync([package]); return; }
                if (command == "enable") await AdapterCallAsync("EnableAdapter", new { id = package.Id, enabled });
                else if (command == "rollback") await AdapterCallAsync("RollbackAdapter", new { id = package.Id });
                await ReloadAdaptersAsync();
            });
        };
        adapterSelect.Checked += (_, _) => RenderAdapterCatalog(); adapterSelect.Unchecked += (_, _) => RenderAdapterCatalog();
        adapterFilter.SelectedItem = adapterFilter.Items[0];
        adapterFilter.SelectionChanged += (_, _) => RenderAdapterCatalog();
        adapterPackages.ItemsSource=adapterRows;
        adapterPackages.SelectionChanged += (_, _) => UpdateAdapterSelection();

        adapterSelectionEnabled.Toggled += async (_, _) => { if (!renderingAdapters) await RunAsync(() => ChangeSelectedAdaptersAsync(false, adapterSelectionEnabled.IsOn)); };
        adapterSelectAll.Click += (_, _) => { if (adapterPackages.SelectedItems.Count == adapterPackages.Items.Count) adapterPackages.SelectedItems.Clear(); else adapterPackages.SelectAll(); };
        adapterRemoveSelected.Click += async (_, _) => await RunAsync(() => ChangeSelectedAdaptersAsync(true));
        settingsPages["adapters"] = adaptersPage;
    }
    private async Task RefreshAdapterCatalogAsync()
    {
        if(!backend.Running&&adapterPreviewRpc is null)return;
        try
        {
            adapterCatalog=(await AdapterCallAsync("ListAdapters")).Deserialize<List<AdapterPackage>>(BackendClient.Json)??[];
            RenderAdapterCatalog();
        }
        catch(Exception e){ShowMessage(e.Message);}
    }
    private void RenderAdapterCatalog()
    {
        var selected=SelectedAdapters().Select(p=>p.Id).ToHashSet();renderingAdapters=true;
        try
        {
            var choosing=adapterSelect.IsChecked==true;
            adapterPackages.SelectionMode=choosing?ListViewSelectionMode.Multiple:ListViewSelectionMode.None;adapterPackages.IsItemClickEnabled=false;
            adapterSelectionBar.Visibility=choosing?Visibility.Visible:Visibility.Collapsed;
            var filter = adapterFilter.SelectedItem is null ? 0 : adapterFilter.Items.IndexOf(adapterFilter.SelectedItem);
            var rows = Devices.AdapterBatchRules.Sort(adapterCatalog.Where(p => filter == 0 || p.Enabled == (filter == 1)), adapterSort, adapterSortDescending)
                .Select(package => adapterRows.FirstOrDefault(row=>ReferenceEquals(row.Package,package)&&row.Choosing==choosing)??new Pages.AdapterPackageRow(package,choosing)).ToList();
            ViewModels.CollectionReconciler.Apply(adapterRows,rows,row=>row.Package.Id);
            adaptersPage.SetCounts(adapterCatalog);
            if (choosing) foreach (var row in rows.Where(row => selected.Contains(row.Package.Id)&&!adapterPackages.SelectedItems.Contains(row))) adapterPackages.SelectedItems.Add(row);
        }
        finally{renderingAdapters=false;UpdateAdapterSelection();}
    }
    private List<AdapterPackage> SelectedAdapters()=>adapterPackages.SelectedItems.OfType<Pages.AdapterPackageRow>().Select(row=>row.Package).ToList();
    private void UpdateAdapterSelection()
    {
        if(renderingAdapters)return;renderingAdapters=true;
        try
        {
            var selected=SelectedAdapters();var choosing=adapterSelect.IsChecked==true;
            adapterCount.Text=choosing?UiText.Format("SelectedCount",selected.Count,adapterPackages.Items.Count):adapterPackages.Items.Count==0?UiText.Get("NoAdapters"):UiText.Format("AdapterCount",adapterPackages.Items.Count);
            adapterSelectionEnabled.IsEnabled=adapterRemoveSelected.IsEnabled=selected.Count>0;
            adapterSelectionEnabled.IsOn=selected.Any(p=>p.Enabled);
            adapterSelectAll.IsEnabled=adapterPackages.Items.Count>0;
            ToolTipService.SetToolTip(adapterSelectionEnabled,selected.Any(p=>p.Enabled)&&selected.Any(p=>!p.Enabled)?"部分已启用；关闭以停用全部所选":"启用所选适配包");
            ToolTipService.SetToolTip(adapterSelectAll,selected.Count==adapterPackages.Items.Count?"取消全选":"全选");
        }
        finally{renderingAdapters=false;}
    }
    private Task ShowAdapterInformationAsync(AdapterPackage package)=>ShowInformationAsync(package.Name,
        "**版本**："+package.Version+"\n\n**适配格式**：v"+package.FormatVersion+"\n\n**SDK**："+(package.SdkVersion.Length>0?package.SdkVersion:"未声明")+
        (package.Author.Length>0?"\n\n**作者**："+package.Author:"")+"\n\n**型号**："+string.Join("、",package.Models)+"\n\n"+(package.Readme.Length>0?package.Readme:"未提供 README。"),markdown:true);
    private async Task ReloadAdaptersAsync(){if(adapterPreviewRpc is null)await LoadDevicesAsync();await RefreshAdapterCatalogAsync();}
    private async void OnSelectAdapter(object sender,RoutedEventArgs args)
    {
        if(selectedDevice is not {} device||busy)return;
        await RunAsync(async()=>
        {
            var packages=(await AdapterCallAsync("ListAdapters")).Deserialize<List<AdapterPackage>>(BackendClient.Json)??[];
            var choices=new[]{new AdapterPackage("$builtin","默认面板","",[],true,false)}.Concat(packages.Where(p=>p.Enabled&&p.Models.Contains(device.Model))).ToList();
            var dialog=new Dialogs.ChoiceDialog {XamlRoot=Root.XamlRoot,Title=MiHome.WinUI.Infrastructure.UiText.Get("S30ada4bf5edd"),PrimaryButtonText=MiHome.WinUI.Infrastructure.UiText.Get("S63c73c4730f4")};
            var rows=choices.Select(package=>new Dialogs.ChoiceItem(package,package.Name)).ToList();
            dialog.ChoiceList.ItemsSource=rows;dialog.ChoiceList.SelectedItem=rows.FirstOrDefault(row=>((AdapterPackage)row.Value).Id==device.Adapter?.Id)??rows[0];
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary||dialog.ChoiceList.SelectedItem is not Dialogs.ChoiceItem selected)return;
            await AdapterCallAsync("SetDeviceAdapter",new{deviceId=device.Id,id=((AdapterPackage)selected.Value).Id});
            await LoadDevicesAsync();
            if(browser.AllDevices.FirstOrDefault(d=>d.Id==device.Id) is {} updated)await OpenDeviceAsync(updated);
        });
    }
    private async void OnExportAdapterContext(object sender,RoutedEventArgs args)
    {
        if(selectedDevice is not {} device||busy)return;
        await RunAsync(async()=>
        {
            var dialog=new Dialogs.ExportContextDialog {XamlRoot=Root.XamlRoot};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var result=await AdapterCallAsync("ExportAdapterContext",new{deviceId=device.Id,includeReadings=dialog.IncludeSamples});
            var picker=new Windows.Storage.Pickers.FileSavePicker {SuggestedFileName=device.Model+"-spec"};picker.FileTypeChoices.Add("JSON",new[]{".json"});
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSaveFileAsync();if(file is not null)await Windows.Storage.FileIO.WriteTextAsync(file,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        });
    }
    private sealed class AdapterPreviewService(DeviceSchema schema,int mode,DeviceAdapter adapter,bool embedded=false):IDeviceService,IDeviceActionService,IEmbeddedActionPreview
    {
        public bool EmbeddedPreview=>embedded;
        public bool IsSimulation => true;
        private readonly Dictionary<(int,int),object> values = schema.Properties.Where(p=>p.Readable).ToDictionary(p=>(p.Siid,p.Piid),p=>
            p.Type=="bool"?(object)(mode!=1):p.Values.Count>0?p.Values[0].Value:p.Type=="string"?"示例":p.Name.Contains("Fault",StringComparison.OrdinalIgnoreCase)?0d:p.Range.Length>0?p.Range[0]:0d);
        public Task<ActionResult> InvokeActionAsync(ActionRequest request)
        {
            if(mode is 2 or 3)throw new BridgeException("preview","模拟设备暂不可用。");
            var action=adapter.Actions.Single(a=>a.Id==request.ActionId).Action;
            var outputs=action.Outputs.Select(p=>JsonSerializer.SerializeToElement(p.Type=="bool"?(object)false:p.Values.Count>0?p.Values[0].Value:p.Type=="string"?"模拟返回":p.Range.Length>0?p.Range[0]:0d)).ToList();
            return Task.FromResult(new ActionResult("accepted","模拟请求已接受，不连接设备。",outputs));
        }
        public Task<DeviceSchema> GetSchemaAsync(string id)=>Task.FromResult(schema);
        public Task<JsonElement> ReadAsync(string id)=>Task.FromResult(JsonSerializer.SerializeToElement(mode is 2 or 3?[]:values.Select(v=>new{siid=v.Key.Item1,piid=v.Key.Item2,code=0,value=v.Value}).ToArray()));
        public Task WriteAsync(PropertyKey key,object? value)
        {
            if(mode is 2 or 3) throw new BridgeException("preview", "模拟设备暂不可用。");
            values[(key.Siid,key.Piid)]=value ?? throw new BridgeException("preview", "模拟值不能为空。"); return Task.CompletedTask;
        }
    }
}
