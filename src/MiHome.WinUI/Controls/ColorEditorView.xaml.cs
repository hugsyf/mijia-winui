using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class ColorEditorView : UserControl
{
    internal ColorPicker Picker => ColorPicker;
    internal Border Swatch => ColorSwatch;
    internal Button ColorButton => ChooseColor;
    internal Flyout ColorFlyout => Flyout;
    internal Button CancelButton => Cancel;
    internal Button ApplyButton => Apply;
    public ColorEditorView() => InitializeComponent();
}
