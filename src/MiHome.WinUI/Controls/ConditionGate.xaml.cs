using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class ConditionGate : UserControl
{
    public static readonly DependencyProperty ChildProperty=DependencyProperty.Register(nameof(Child),typeof(UIElement),typeof(ConditionGate),new PropertyMetadata(null));
    public UIElement? Child {get=>(UIElement?)GetValue(ChildProperty);set=>SetValue(ChildProperty,value);}
    public ConditionGate()=>InitializeComponent();
}
