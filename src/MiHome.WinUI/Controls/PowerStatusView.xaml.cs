using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class PowerStatusView : UserControl
{
    internal TextBlock StateLabel => Status;
    internal TextBlock HumidityLabel => Humidity;
    internal ToggleSwitch PowerToggle => Toggle;
    internal StackPanel AdditionalReadings => Footer;
    public PowerStatusView() => InitializeComponent();
    internal void Configure(string label, bool humidity, bool surface)
    {
        Label.Text=humidity?"当前湿度":label;
        Label.Style=(Style)Application.Current.Resources[humidity?"DeviceSecondaryTextStyle":"BodyStrongTextBlockStyle"];
        Humidity.Visibility=humidity?Visibility.Visible:Visibility.Collapsed;
        if (!surface) { Surface.Padding=new Thickness(0);Surface.BorderThickness=new Thickness(0);Surface.Background=null; }
    }
}
