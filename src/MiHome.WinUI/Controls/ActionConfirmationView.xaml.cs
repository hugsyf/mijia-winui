using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class ActionConfirmationView : UserControl
{
    internal StackPanel Inputs => Parameters;
    internal TextBlock ErrorLabel => Invalid;
    internal Button ExecuteButton => Execute;
    internal Button CancelButton => Cancel;
    internal void SetIdentity(string device,string description) { Device.Text=device; Description.Text=description; }
    internal void ShowCommands() => Commands.Visibility=Visibility.Visible;
    public ActionConfirmationView() => InitializeComponent();
}
