using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class ExportContextDialog : ContentDialog
{
    internal bool IncludeSamples => Samples.IsChecked == true;
    public ExportContextDialog() => InitializeComponent();
}
