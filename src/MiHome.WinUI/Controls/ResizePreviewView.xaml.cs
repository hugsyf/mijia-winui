using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class ResizePreviewView : UserControl
{
    internal TextBlock SizeLabel => Label;
    public ResizePreviewView() => InitializeComponent();
}
