using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed class DeviceGridView : GridView
{
    private bool controlInteraction;
    public bool ConsumeControlInteraction(){var result=controlInteraction;controlInteraction=false;return result;}
    public DeviceGridView()
    {
        AddHandler(PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>controlInteraction=Interactive(e.OriginalSource as DependencyObject)),true);
        AddHandler(KeyDownEvent,new Microsoft.UI.Xaml.Input.KeyEventHandler((_,e)=>controlInteraction=Interactive(e.OriginalSource as DependencyObject)),true);
    }
    private static bool Interactive(DependencyObject? source)
    {
        for(var node=source;node is not null;node=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if(node is Slider or ComboBox or ToggleSwitch or TextBox or NumberBox or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or Microsoft.UI.Xaml.Controls.Primitives.Thumb)return true;
            if(node is DeviceCardView or GridViewItem)return false;
        }
        return false;
    }
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not GridViewItem container || item is not DeviceTile tile) return;
        var size = tile.Size;
        VariableSizedWrapGrid.SetRowSpan(container, size == "compact" ? 1 : 2);
        VariableSizedWrapGrid.SetColumnSpan(container, size == "expanded" ? 2 : 1);
        AutomationProperties.SetName(container, tile.AccessibleName);
    }
}
