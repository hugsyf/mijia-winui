using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class AdapterProgressDialog : ContentDialog
{
    internal TextBlock StatusLabel => Status;
    internal ProgressBar ProgressIndicator => Progress;
    public AdapterProgressDialog() => InitializeComponent();
}
