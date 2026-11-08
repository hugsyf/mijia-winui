using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class UpdateDownloadDialog : ContentDialog
{
    public UpdateDownloadDialog()=>InitializeComponent();
    internal void SetProgress(double value){Progress.Value=value;Status.Text=$"{value:0}%";}
}
