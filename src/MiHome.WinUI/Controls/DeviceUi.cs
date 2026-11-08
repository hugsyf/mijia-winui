using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Data;
namespace MiHome.WinUI.Controls;

internal static class DeviceUi
{
    public static ScrollViewer Scroll(UIElement content,double maximumHeight=double.PositiveInfinity) => new()
    {
        Content=content,MaxHeight=maximumHeight,Padding=new Thickness(0,0,20,0),
        HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
        VerticalScrollMode=ScrollMode.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,
        ZoomMode=ZoomMode.Disabled,FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.Normal
    };
    public static Border Surface(UIElement content) => new() { Style = (Style)Application.Current.Resources["DeviceSurfaceStyle"], Child = content };
    public static TextBlock Text(string text, string style = "BodyTextBlockStyle") => new()
    { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources[style] };
    public static TextBlock Secondary(string text) => Text(text, "DeviceSecondaryTextStyle");
    public static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    public static Viewbox Icon(Geometry geometry, Control owner, double size = 24)
    {
        var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty, new Binding { Source = owner, Path = new PropertyPath("Foreground") });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
    public static ProductIconView DeviceIcon(ViewModels.DeviceTile tile,Control owner,double size=40)
    {
        var view=new ProductIconView {Tile=tile,IconSize=size};
        view.SetBinding(Control.ForegroundProperty,new Binding {Source=owner,Path=new PropertyPath("Foreground")});
        return view;
    }
}
