using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
namespace MiHome.WinUI.Controls;
public sealed partial class IconActionButton : Button
{
    public IconActionButton() => InitializeComponent();
    internal IconActionButton(string glyph, string label) : this()
    { Icon.Glyph=glyph; AutomationProperties.SetName(this,label); ToolTipService.SetToolTip(this,label); }
}
