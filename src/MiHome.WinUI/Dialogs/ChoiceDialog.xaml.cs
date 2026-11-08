using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Dialogs;
public sealed partial class ChoiceDialog : ContentDialog
{
    internal ListView ChoiceList => Choices;
    public ChoiceDialog() => InitializeComponent();
}
public sealed record ChoiceItem(object Value, string Label);
