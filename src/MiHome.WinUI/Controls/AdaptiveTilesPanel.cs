using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
namespace MiHome.WinUI.Controls;

// Layout calculations belong in code; item structure belongs in the XAML template.
public sealed class AdaptiveTilesPanel : Panel
{
    public double MinimumItemWidth { get; set; } = 240;
    public int MaximumColumns { get; set; } = 2;
    public double Spacing { get; set; } = 10;
    private int Columns(double width) => double.IsInfinity(width) ? 1 : Math.Min(Math.Max(1, Children.Count), Math.Clamp((int)((width + Spacing) / (MinimumItemWidth + Spacing)), 1, MaximumColumns));
    protected override Size MeasureOverride(Size available)
    {
        var columns = Columns(available.Width);
        var width = double.IsInfinity(available.Width) ? MinimumItemWidth : Math.Max(0, (available.Width - (columns - 1) * Spacing) / columns);
        double height = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(width, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == Children.Count - 1) { height += rowHeight + (i >= columns ? Spacing : 0); rowHeight = 0; }
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var columns = Columns(final.Width);
        var width = Math.Max(0, (final.Width - (columns - 1) * Spacing) / columns);
        double top = 0;
        for (var start = 0; start < Children.Count; start += columns)
        {
            var height = Children.Skip(start).Take(columns).Max(child => child.DesiredSize.Height);
            for (var col = 0; col < columns && start + col < Children.Count; col++)
                Children[start + col].Arrange(new Rect(col * (width + Spacing), top, width, height));
            top += height + Spacing;
        }
        return final;
    }
}
