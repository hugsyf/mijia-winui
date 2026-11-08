using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class MetricView : UserControl
{
    internal TextBlock ValueLabel => Reading;
    internal TextBlock CaptionLabel => Caption;
    public MetricView() => InitializeComponent();
    internal MetricView(string label, bool compact=false, bool surface=true) : this()
    {
        Label.Text=label;
        if (!surface || compact) { Surface.Background=null; Surface.BorderThickness=new Thickness(0); Surface.Padding=compact?new Thickness(4):new Thickness(0); }
        if (compact)
        {
            Grid.SetRow(Reading,0);Grid.SetColumn(Reading,1);Layout.RowSpacing=0;
            Reading.Style=(Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        }
    }
}
