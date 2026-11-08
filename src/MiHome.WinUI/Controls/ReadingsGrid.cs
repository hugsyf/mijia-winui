using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;

internal sealed class ReadingsGrid : Grid
{
    public ReadingsGrid(IEnumerable<FrameworkElement> items)
    {
        ColumnSpacing = 12; RowSpacing = 12;
        foreach (var item in items)
        {
            Children.Add(item);
            item.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => ArrangeReadings());
        }
        SizeChanged += (_, _) => ArrangeReadings();
        ArrangeReadings();
    }
    private void ArrangeReadings()
    {
        var columns = ActualWidth >= 360 ? 2 : 1;
        ColumnDefinitions.Clear(); RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) ColumnDefinitions.Add(new());
        var index = 0;
        foreach (var item in Children.OfType<FrameworkElement>().Where(i => i.Visibility == Visibility.Visible))
        {
            if (index % columns == 0) RowDefinitions.Add(new() { Height = GridLength.Auto });
            SetColumn(item, index % columns); SetRow(item, index / columns); index++;
        }
    }
}
