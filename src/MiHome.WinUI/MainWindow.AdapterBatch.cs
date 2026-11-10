using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private sealed record AdapterWorkResult(string Name,string Detail,bool Success,bool Skipped=false);
    private sealed record AdapterImportFile(string Path,JsonElement? Inspection,string? Error=null)
    {
        public string FileName=>System.IO.Path.GetFileName(Path);
        public DeviceAdapter? Definition=>Inspection?.GetProperty("definition").Deserialize<DeviceAdapter>(BackendClient.Json);
    }
    private async Task<List<AdapterWorkResult>> RunAdapterItemsAsync<T>(string title,IReadOnlyList<T> items,Func<T,string> name,Func<T,Task<string?>> action)
    {
        var stop=false;var finished=false;
        var dialog=new Dialogs.AdapterProgressDialog {XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme,Title=title,CloseButtonText=UiText.Get("AdapterStopRemaining")};
        var label=dialog.StatusLabel;var progress=dialog.ProgressIndicator;progress.Maximum=Math.Max(1,items.Count);
        dialog.CloseButtonClick+=(_,e)=>{e.Cancel=true;stop=true;dialog.CloseButtonText=MiHome.WinUI.Infrastructure.UiText.Get("S8a99be840844");label.Text=MiHome.WinUI.Infrastructure.UiText.Get("Sd9f0cd172d82");};
        dialog.Closing+=(_,e)=>{if(!finished){e.Cancel=true;stop=true;dialog.CloseButtonText=MiHome.WinUI.Infrastructure.UiText.Get("S8a99be840844");}};
        var shown=dialog.ShowAsync();var results=new List<AdapterWorkResult>();
        try
        {
            for(var i=0;i<items.Count;i++)
            {
                if(stop){results.AddRange(items.Skip(i).Select(item=>new AdapterWorkResult(name(item),"未执行",false,true)));break;}
                var item=items[i];label.Text=$"{i+1} / {items.Count} · {name(item)}";
                try {var detail=await action(item);results.Add(new(name(item),detail??"已有专属面板，已跳过",detail is not null,detail is null));}
                catch(BridgeException e) when(e.Code=="auth"){throw;}
                catch(Exception e){results.Add(new(name(item),e.Message,false));}
                progress.Value=i+1;
            }
        }
        finally{finished=true;dialog.Hide();await shown;}
        return results;
    }
    private void ShowAdapterResults(string title,List<AdapterWorkResult> results)
    {
        var success=results.Count(r=>r.Success);var skipped=results.Count(r=>r.Skipped);var failed=results.Count-success-skipped;
        adapterResult.Title=title;adapterResult.Message=Infrastructure.UiText.Format("CompletedCount",success)+(failed>0?" · "+Infrastructure.UiText.Format("FailedCount",failed):"")+(skipped>0?" · "+Infrastructure.UiText.Format("SkippedCount",skipped):"");
        adapterResult.Severity=failed>0?InfoBarSeverity.Warning:InfoBarSeverity.Success;
        var details=AdapterIconButton("\uE946","查看操作结果");details.Click+=async(_,_)=>await ShowInformationAsync(title,string.Join("\n\n",results.Select(r=>r.Name+"\n"+r.Detail)));
        adapterResult.ActionButton=details;adapterResult.IsOpen=true;
    }
    private async Task ChangeSelectedAdaptersAsync(bool remove,bool enabled=false)
    {
        var selected=SelectedAdapters();if(selected.Count==0)return;
        if(remove){await RemoveAdaptersAsync(selected);return;}
        var targets=selected.Where(p=>p.Enabled!=enabled).ToList();
        if(targets.Count==0){UpdateAdapterSelection();return;}
        var results=await RunAdapterItemsAsync(enabled?"启用适配包":"停用适配包",targets,p=>p.Name,async p=>{await AdapterCallAsync("EnableAdapter",new{id=p.Id,enabled});return enabled?"已启用":"已停用";});
        await ReloadAdaptersAsync();ShowAdapterResults(enabled?"启用适配包":"停用适配包",results);
    }
    private async Task RemoveAdaptersAsync(List<AdapterPackage> selected)
    {
        var confirmation = new Dialogs.MessageDialog { XamlRoot=Root.XamlRoot, RequestedTheme=Root.ActualTheme, Title=UiText.Format("AdapterRemoveConfirmation",selected.Count), PrimaryButtonText=UiText.Get("S06bc14b60f35") };
        confirmation.SetBody(DocumentPresenter.Plain(string.Join("\n",selected.Select(p=>p.Name))+"\n\n相关设备将恢复默认面板。"),300);
        if(await confirmation.ShowAsync()!=ContentDialogResult.Primary)return;
        var results=await RunAdapterItemsAsync("卸载适配包",selected,p=>p.Name,async p=>{await AdapterCallAsync("RemoveAdapter",new{id=p.Id});return "已卸载";});
        await ReloadAdaptersAsync();ShowAdapterResults("卸载适配包",results);
    }
    private async Task ExportDefaultAdaptersAsync()
    {
        var candidates=AdapterBatchRules.ExportCandidates(browser.AllDevices);
        if(candidates.Count==0){await ShowInformationAsync(MiHome.WinUI.Infrastructure.UiText.Get("Scdb70be0681a"),"当前家庭没有使用默认面板的设备。");return;}
        using var buffer=new MemoryStream();var exported=0;
        List<AdapterWorkResult> results;
        using(var archive=new ZipArchive(buffer,ZipArchiveMode.Create,true))
        {
            results=await RunAdapterItemsAsync("导出未适配资料",candidates,d=>d.Model,async device=>
            {
                var schema=await (diagnosticService??backend).GetSchemaAsync(device.Id);
                if(!AdapterBatchRules.UsesDefaultPanel(device,schema))return null;
                if(string.IsNullOrWhiteSpace(device.SpecType))throw new InvalidOperationException("设备未提供公开规格。");
                var context=await AdapterCallAsync("ExportAdapterContext",new{deviceId=device.Id,includeReadings=false});
                var json=JsonSerializer.Serialize(context,new JsonSerializerOptions {WriteIndented=true});
                if(Encoding.UTF8.GetByteCount(json)>5*1024*1024||buffer.Length>50*1024*1024)throw new InvalidOperationException("资料过大，请单独导出此型号。");
                var safeName=new string(device.Model.Select(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'?c:'_').Take(120).ToArray());
                var entry=archive.CreateEntry($"{++exported:D3}-{safeName}.json",CompressionLevel.Optimal);
                using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));await writer.WriteAsync(json);
                return entry.FullName;
            });
            if(exported>0)
            {
                var manifest=archive.CreateEntry("README.txt");using var writer=new StreamWriter(manifest.Open(),new UTF8Encoding(false));
                await writer.WriteAsync("设备适配资料\n\n每个 JSON 对应一个型号与规格。将 JSON 与适配 SDK 交给 Agent。\n此包不含设备名称、房间、设备 ID、账号或读数。\n");
            }
        }
        if(exported>0)
        {
            var picker=new Windows.Storage.Pickers.FileSavePicker {SuggestedFileName="mihome-adapter-contexts"};picker.FileTypeChoices.Add("ZIP",new[]{".zip"});
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSaveFileAsync();
            if(file is null){ShowAdapterResults("导出已取消",results.Select(r=>r.Success?r with{Success=false,Skipped=true,Detail="未保存"}:r).ToList());return;}
            using var output=await file.OpenStreamForWriteAsync();output.SetLength(0);buffer.Position=0;await buffer.CopyToAsync(output);
        }
        ShowAdapterResults("导出未适配资料",results);
    }
    private async Task ImportAdapterAsync()
    {
        if(adapterPreviewImports is {} paths){await ImportAdapterPathsAsync(paths);return;}
        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".mihome-adapter");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files=await picker.PickMultipleFilesAsync();if(files.Count==0)return;
        if(files.Count>100){await ShowInformationAsync(MiHome.WinUI.Infrastructure.UiText.Get("S66e5a1c5807e"),"每次最多选择 100 个文件。");return;}
        await ImportAdapterPathsAsync(files.Select(f=>f.Path).ToList());
    }
    private async Task ImportAdapterPathsAsync(List<string> paths)
    {
        var inspected=new List<AdapterImportFile>();
        var checks=await RunAdapterItemsAsync("检查适配包",paths,Path.GetFileName,async path=>
        {
            try{inspected.Add(new(path,await AdapterCallAsync("InspectAdapter",new{path})));return "校验通过";}
            catch(BridgeException e) when(e.Code=="auth"){throw;}
            catch(Exception e){inspected.Add(new(path,null,e.Message));throw;}
        });
        if(inspected.Count==0){ShowAdapterResults("导入已取消",checks);return;}
        var duplicateIds=AdapterBatchRules.DuplicateIds(inspected.Where(f=>f.Inspection is not null).Select(f=>f.Definition!.Id));
        inspected=inspected.Select(f=>f.Definition is {} d&&duplicateIds.Contains(d.Id)?f with{Error="适配包标识重复，请每次只选择一个版本。"}:f).ToList();
        var dialog = new Dialogs.AdapterImportDialog { XamlRoot=Root.XamlRoot, RequestedTheme=Root.ActualTheme };
        dialog.SetMaximumHeight(Math.Max(120,Math.Min(500,Root.XamlRoot.Size.Height-220)));
        var list=dialog.FileList;var apply=dialog.ApplyToggle;var warning=dialog.WarningLabel;
        var rows=inspected.Select(file=>
        {
            var definition=file.Definition;var installed=adapterCatalog.FirstOrDefault(p=>p.Id==definition?.Id);
            return new Dialogs.AdapterImportRow(file,definition is null?file.FileName:definition.Name+"  "+definition.Version,
                file.Error??file.FileName+(installed is not null?" · 替换 "+installed.Version:""),file.Error is null);
        }).ToList();
        list.ItemsSource=rows;foreach(var row in rows.Where(row=>row.CanInstall))list.SelectedItems.Add(row);
        dialog.PreviewRequested+=async(row,host)=>
        {
            try{host.Content=await CreateAdapterPreviewAsync(((AdapterImportFile)row.Value).Inspection!.Value);}
            catch(Exception e){host.Content=DocumentPresenter.Plain(e.Message);}
        };
        List<AdapterImportFile> Selected()=>list.SelectedItems.OfType<Dialogs.AdapterImportRow>().Where(row=>row.CanInstall).Select(row=>(AdapterImportFile)row.Value).ToList();
        bool ValidateSelection()
        {
            var selected=Selected();var overlap=apply.IsOn&&AdapterBatchRules.OverlappingModels(selected.Select(f=>f.Inspection!.Value.GetProperty("document").GetProperty("models").EnumerateArray().Select(m=>m.GetString()!)));
            warning.Text=overlap?Infrastructure.UiText.Get("PackageOverlap"):"";warning.Visibility=overlap?Visibility.Visible:Visibility.Collapsed;
            dialog.PrimaryButtonText=Infrastructure.UiText.Format("InstallCount",selected.Count);return selected.Count>0&&!overlap;
        }
        void UpdateSelection()=>dialog.IsPrimaryButtonEnabled=ValidateSelection();
        list.SelectionChanged+=(_,_)=>UpdateSelection();apply.Toggled+=(_,_)=>UpdateSelection();UpdateSelection();
        dialog.PrimaryButtonClick+=(_,e)=>e.Cancel=!ValidateSelection();
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var selectedFiles=Selected();var applyModels=apply.IsOn;
        var results=await RunAdapterItemsAsync("安装适配包",selectedFiles,f=>f.Definition!.Name,async file=>
        {
            await AdapterCallAsync("InstallAdapter",new{path=file.Path,applyModels,expectedHash=file.Inspection!.Value.GetProperty("packageHash").GetString()});return "已安装";
        });
        results.AddRange(inspected.Where(f=>f.Error is not null).Select(f=>new AdapterWorkResult(f.FileName,f.Error!,false)));
        await ReloadAdaptersAsync();ShowAdapterResults("导入适配包",results);
    }
    private async Task<FrameworkElement> CreateAdapterPreviewAsync(JsonElement result)
    {
        var preview=result.GetProperty("preview");var adapter=result.GetProperty("definition").Deserialize<DeviceAdapter>(BackendClient.Json)!;
        var schema=preview.GetProperty("schema").Deserialize<DeviceSchema>(BackendClient.Json)!;
        var device=new DeviceIdentity("preview",adapter.Name,preview.GetProperty("model").GetString()!,preview.GetProperty("specType").GetString()!,"preview","预览",true){Adapter=adapter};
        var content=new AdapterPreviewView {Width=Math.Max(240,Math.Min(480,Root.XamlRoot.Size.Width-160))};
        content.SetIdentity(adapter.Name,"可控制 "+adapter.Bindings.Count(b=>b.AllowWrite)+" 项 · 动作 "+adapter.Actions.Count);
        var modes=content.ModeOptions;var views=content.ViewOptions;var host=content.Host;
        async Task Render()
        {
            var mode=modes.Items.IndexOf(modes.SelectedItem);var presentation=views.Items.IndexOf(views.SelectedItem);var service=new AdapterPreviewService(schema,mode,adapter,embedded:true);var mock=device with{Online=mode!=2};
            if(presentation is 0 or 3)
            {
                var state=new DeviceCardState(mock.Id,schema,await service.ReadAsync(mock.Id),DateTimeOffset.Now,null);
                var tile=new MiHome.WinUI.ViewModels.DeviceTile(mock,state) {PresentationSize=presentation==3?"expanded":"standard"};
                var cardPreview=new AdapterCardPreview();var card=cardPreview.DeviceCard;card.DataContext=tile;card.IsEnabled=presentation==3;var feedback=cardPreview.FeedbackLabel;host.Content=cardPreview;
                card.Configure(service,s=>tile.UpdateSnapshot(s),message=>feedback.Text=message);card.EnsureContent();
            }
            else {var panel=new AdapterPanel(mock,schema,service,true);panel.SetExpanded(presentation==2);host.Content=panel;await panel.RefreshAsync();}
        }
        modes.SelectionChanged+=async(_,_)=>await Render();views.SelectionChanged+=async(_,_)=>await Render();await Render();return content;
    }
}
