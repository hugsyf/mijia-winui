using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
namespace MiHome.WinUI.Controls;

[ContentProperty(Name = nameof(Action))]
public sealed partial class SettingsRow : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata("", OnDescriptionChanged));
    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(UIElement), typeof(SettingsRow), new PropertyMetadata(null));
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public UIElement? Action { get => (UIElement?)GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public SettingsRow() => InitializeComponent();
    private static void OnDescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var row = (SettingsRow)sender;
        row.DescriptionText.Visibility = string.IsNullOrWhiteSpace((string)args.NewValue) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 480;
        Layout.RowSpacing = narrow ? 12 : 0;
        Grid.SetRow(ActionHost, narrow ? 1 : 0);
        Grid.SetColumn(ActionHost, narrow ? 0 : 1);
        Grid.SetColumnSpan(LabelHost, narrow ? 2 : 1);
        Grid.SetColumnSpan(ActionHost, narrow ? 2 : 1);
    }
}
