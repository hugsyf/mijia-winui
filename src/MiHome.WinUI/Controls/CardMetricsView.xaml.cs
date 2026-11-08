using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class CardMetricsView : UserControl
{
    public CardMetricsView() => InitializeComponent();
    internal void SetReadings(IEnumerable<CardMetric> readings) => Readings.ItemsSource=readings;
}
public sealed record CardMetric(string Label,string Value,string Unit="",double FontSize=26);
