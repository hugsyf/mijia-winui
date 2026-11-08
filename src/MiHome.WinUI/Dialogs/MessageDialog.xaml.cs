using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class MessageDialog : ContentDialog
{
    public MessageDialog() => InitializeComponent();
    internal void SetBody(UIElement content, double height) { Document.Content = content; Scroller.MaxHeight = height; }
}
