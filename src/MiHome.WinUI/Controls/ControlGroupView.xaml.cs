using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class ControlGroupView : UserControl
{
    internal StackPanel ItemsHost => Items;
    public ControlGroupView() => InitializeComponent();
}
