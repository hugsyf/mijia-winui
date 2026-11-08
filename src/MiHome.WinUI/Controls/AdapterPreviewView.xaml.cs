using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class AdapterPreviewView : UserControl
{
    internal SelectorBar ModeOptions => Modes;
    internal SelectorBar ViewOptions => Views;
    internal ContentControl Host => PreviewHost;
    internal void SetIdentity(string name, string capabilities) { NameLabel.Text = name; CapabilityLabel.Text = capabilities; }
    public AdapterPreviewView() { InitializeComponent(); Modes.SelectedItem = Modes.Items[0]; Views.SelectedItem = Views.Items[1]; }
}
