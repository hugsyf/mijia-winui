using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class ChoiceDialog : ContentDialog
{
    internal ListView ChoiceList => Choices;
    public ChoiceDialog() => InitializeComponent();
    internal void SetContext(string label)
    {
        ContextLabel.Text = label;
        ContextLabel.Visibility = Visibility.Visible;
        Choices.MaxHeight = Math.Clamp(XamlRoot.Size.Height - 230, 120, 400);
    }
}
public sealed record ChoiceItem(object Value, string Label);
