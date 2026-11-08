using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class HumidifierCardSummary : UserControl
{
    public HumidifierCardSummary() => InitializeComponent();
    internal void SetReading(string state, FrameworkElement metric) { State.Text=state;Metric.Content=metric; }
}
