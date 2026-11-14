using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Core;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private HomePreferences homePreferences=new([],[]);
    private HomeOverviewView? homeView;
    private bool homeLightingFilter;
    private void InitializeHome()
    {
        homeView=HomeOverviewHost;
        homeView.Configure((DataTemplate)Root.Resources["DeviceTemplate"],(Style)Root.Resources["DeviceContainerStyle"]);
        homeView.LightingCommandRequested+=async(room,on)=>await SetHomeLightingAsync(room,on);
        homeView.DeviceRequested+=async tile=>await RunAsync(()=>OpenDeviceAsync(tile.Device,true));
        homeView.PinsRequested+=async()=>await ChooseHomePinsAsync();
        homeView.SecurityRequested+=async item=>await OpenHomeSecurityAsync(item);
        homeView.SourceRequested+=async room=>await ChooseHomeSourceAsync(room);
        homeView.LightingRequested+=room=>
        {
            Navigation.SelectedItem=Navigation.MenuItems[1];
            changingRooms=true;Rooms.SelectedItem=Rooms.Items.FirstOrDefault(item=>(item.Tag as Room)?.Id==room.RoomId)??Rooms.Items.FirstOrDefault();changingRooms=false;
            Search.Text="";homeLightingFilter=true;ApplyFilter();
        };
    }
    private async Task SetHomeLightingAsync(string? roomId,bool on)
    {
        if(busy||!authenticated)return;
        var tiles=browser.HomeTiles;
        await RunAsync(async()=>
        {
            if(detailOpen)Back();
            ShowMessage(on?"正在开启照明…":"正在关闭照明…",InfoBarSeverity.Informational);
            List<LightingResult> results;
            try {results=await new HomeLightingController(backend).ExecuteAsync(tiles,roomId,on,state=>browser.UpdateCardState(state));}
            catch(BridgeException error) when(error.Code=="auth")
            {throw new BridgeException("auth","登录已失效，照明操作已停止。此前已发送的操作不会撤销。");}
            ApplyFilter();
            var confirmed=results.Count(result=>result.Outcome=="confirmed");
            var skipped=results.Count(result=>result.Outcome=="skipped");
            var unconfirmed=results.Count(result=>result.Outcome=="unconfirmed");
            if(results.Count==0){ShowMessage("没有可操作的灯具。");return;}
            ShowMessage($"{(on?"开启":"关闭")}照明：确认 {confirmed} 项"+(skipped>0?$" · 跳过 {skipped} 项":"")+(unconfirmed>0?$" · 未确认 {unconfirmed} 项":""),
                skipped+unconfirmed==0?InfoBarSeverity.Success:InfoBarSeverity.Warning);
            if(skipped+unconfirmed>0)
            {
                await ShowInformationAsync("照明操作结果",string.Join("\n\n",results.Where(result=>result.Outcome!="confirmed").Select(result=>
                    result.Device+(result.Channel.Length>0?" · "+result.Channel:"")+"\n"+(result.Outcome=="skipped"?"已跳过：":"未确认：")+result.Detail)));
            }
        });
    }
    private void UpdateHome()
    {
        homeView?.Show(HomeOverview.Build(browser.HomeTiles,homePreferences));
        if(OverviewView.Visibility==Visibility.Visible)
        {
            PageDescription.Text=homeView?.Headline??"";
            PageDescription.Visibility=PageDescription.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        LightingFilterChip.Visibility=homeLightingFilter?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task SaveHomePreferencesAsync(HomePreferences next)
    {
        if(Homes.SelectedItem is not Home home)return;
        await RunAsync(async()=>
        {
            var saved=await backend.CallAsync("SetHomePreferences",new{homeId=home.Id,preferences=next});
            homePreferences=saved.Deserialize<HomePreferences>(BackendClient.Json)??new([],[]);UpdateHome();
        });
    }
    private async Task ChooseHomePinsAsync()
    {
        if(busy)return;
        var tiles=browser.HomeTiles;
        var dialog=new Dialogs.ChoiceDialog {XamlRoot=Root.XamlRoot,Title="常用设备"};
        var list=dialog.ChoiceList;list.SelectionMode=ListViewSelectionMode.Multiple;
        var rows=tiles.Select(tile=>new Dialogs.ChoiceItem(tile.Device.Id,tile.Name+" · "+tile.Room)).ToList();list.ItemsSource=rows;
        foreach(var row in rows.Where(row=>homePreferences.Pins.Contains((string)row.Value)))list.SelectedItems.Add(row);
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var pins=list.SelectedItems.Cast<Dialogs.ChoiceItem>().Select(item=>(string)item.Value).ToList();
        if(pins.Count>12){ShowMessage("最多固定 12 台设备。");return;}
        await SaveHomePreferencesAsync(homePreferences with{Pins=pins});
    }
    private async Task ChooseHomeSourceAsync(HomeEnvironment room)
    {
        if(busy)return;
        var dialog=new Dialogs.ChoiceDialog {XamlRoot=Root.XamlRoot,Title=room.Room+"的主传感器"};
        var rows=room.Candidates.Select(tile=>new Dialogs.ChoiceItem(tile,tile.Name)).ToList();
        dialog.ChoiceList.ItemsSource=rows;dialog.ChoiceList.SelectedItem=rows.FirstOrDefault(row=>Equals(row.Value,room.Source))??rows.First();
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary||dialog.ChoiceList.SelectedItem is not Dialogs.ChoiceItem selected)return;
        var sources=new Dictionary<string,string>(homePreferences.Sources){[room.RoomId]=((DeviceTile)selected.Value).Device.Id};
        await SaveHomePreferencesAsync(homePreferences with{Sources=sources});
    }
    private async Task OpenHomeSecurityAsync(HomeSecurity security)
    {
        if (busy) return;
        if (security.Members.Count == 1)
        {
            await RunAsync(() => OpenDeviceAsync(security.Tile.Device,true));
            return;
        }
        var homeId = App.Session.Home?.Id;
        var dialog = new Dialogs.ChoiceDialog
        {
            XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme,Title=security.Name,
            PrimaryButtonText=UiText.Get("HomeOpenSensor"),DefaultButton=ContentDialogButton.Primary
        };
        var choices = security.Members.Select(tile => new Dialogs.ChoiceItem(tile, tile.Name + " · " +
            UiText.Get(HomeOverview.Occupancy(tile) is true ? "HomeOccupied" : HomeOverview.Occupancy(tile) is false ? "HomeVacant" : "UnknownState"))).ToList();
        dialog.ChoiceList.ItemsSource=choices;
        dialog.ChoiceList.SelectedItem=choices.FirstOrDefault(choice=>((DeviceTile)choice.Value).Device.Id==security.Tile.Device.Id)??choices[0];
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary || App.Session.Home?.Id!=homeId ||
            dialog.ChoiceList.SelectedItem is not Dialogs.ChoiceItem {Value:DeviceTile selected})return;
        var current=browser.HomeTiles.FirstOrDefault(tile=>tile.Device.Id==selected.Device.Id);
        if(current is not null)await RunAsync(()=>OpenDeviceAsync(current.Device,true));
    }
    private void OnClearLightingFilter(object sender,RoutedEventArgs args){homeLightingFilter=false;ApplyFilter();}
}
