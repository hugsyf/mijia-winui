using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Infrastructure;
namespace MiHome.WinUI;
public sealed partial class MainWindow
{
    private readonly UpdateService updates=new(Path.Combine(DataDirectory,"Updates"));
    private bool updateInProgress;
    private async Task CheckForUpdatesAsync(bool automatic=false)
    {
        if(updateInProgress)return;
        updateInProgress=true;
        aboutPage.SetUpdateChecking(true);
        var userCancelled=false;
        try
        {
            aboutPage.SetUpdateStatus(UiText.Get("UpdateChecking"));
            var release=await updates.CheckAsync(automatic);
            var current=typeof(App).Assembly.GetName().Version!;
            if(release is null){aboutPage.SetUpdateStatus(UiText.Get("UpdateNoRelease"));return;}
            if(!UpdateService.IsNewer(release.Version,current)){aboutPage.SetUpdateStatus(UiText.Get("UpdateCurrent"));return;}
            aboutPage.SetUpdateStatus(UiText.Format("UpdateAvailable",release.Version));
            if(automatic){ShowMessage(UiText.Format("UpdateAvailable",release.Version),InfoBarSeverity.Informational);return;}
            var dialog=new Dialogs.UpdateAvailableDialog {XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme};
            dialog.SetRelease(release,Root.XamlRoot.Size.Height);
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            if(release.Download is null){await Windows.System.Launcher.LaunchUriAsync(new Uri(release.Page));return;}
            using var cancellation=new CancellationTokenSource();
            var progress=new Dialogs.UpdateDownloadDialog {XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme};
            var downloading=true;
            progress.CloseButtonClick+=(_,_)=>{userCancelled=true;cancellation.Cancel();};
            progress.Closed+=(_,_)=>{if(downloading){userCancelled=true;cancellation.Cancel();}};
            var shown=progress.ShowAsync();string installer;
            try{installer=await updates.DownloadAsync(release,new Progress<double>(progress.SetProgress),cancellation.Token);}
            finally{downloading=false;progress.Hide();await shown;}
            var confirm=new Dialogs.InstallUpdateDialog {XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme};
            if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer){UseShellExecute=true});Close();
        }
        catch(OperationCanceledException) when(userCancelled){aboutPage.SetUpdateStatus(UiText.Get("UpdateCancelled"));}
        catch(Exception error){aboutPage.SetUpdateStatus(UiText.Get("UpdateFailed")+" "+error.Message);}
        finally
        {
            updateInProgress=false;
            aboutPage.SetUpdateChecking(false);
        }
    }
}
