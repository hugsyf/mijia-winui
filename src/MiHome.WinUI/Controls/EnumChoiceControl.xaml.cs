using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Controls;

public sealed partial class EnumChoiceControl : UserControl
{
    private readonly List<EnumValue> values = [];
    private bool syncing;
    private JsonElement? currentValue;
    public event Action<JsonElement>? ValueChosen;
    public EnumChoiceControl() => InitializeComponent();
    public EnumChoiceControl(List<EnumValue> values, string presentation = "auto", bool compact = false) : this()
    {
        this.values = values;
        foreach (var value in values)
        {
            var item=new SelectorBarItem { Text=value.DisplayLabel };
            if(compact)item.MinHeight=28;
            bar.Items.Add(item);
        }
        if(compact){bar.MinHeight=28;menu.MinHeight=28;menu.Padding=new Thickness(6,2,6,2);}
        menu.ItemsSource = values.Select(v => v.DisplayLabel).ToList();
        bar.SelectionChanged += (_, _) => Choose(bar.SelectedItem is {} item ? bar.Items.IndexOf(item) : -1);
        menu.SelectionChanged += (_, _) => Choose(menu.SelectedIndex);
        void Layout()
        {
            var widths = bar.Items.Select(item =>
            {
                var text = new TextBlock { Text = item.Text, FontFamily = item.FontFamily, FontSize = item.FontSize, FontWeight = item.FontWeight };
                text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                return text.DesiredSize.Width;
            }).ToArray();
            var desiredWidth = values.Count > 0 ? values.Count * (widths.Max() + 32) : double.PositiveInfinity;
            var segmented = presentation != "dropdown" && values.Count is > 0 and <= 5 && ActualWidth >= desiredWidth;
            if (segmented)
                for (var index = 0; index < bar.Items.Count; index++)
                {
                    var item = bar.Items[index];
                    item.Width = Math.Max(0, ActualWidth - 2) / values.Count;
                    var inset = Math.Max(12, (item.Width - widths[index]) / 2);
                    item.Padding = new Thickness(inset, compact?4:8, inset, compact?4:8);
                    item.HorizontalAlignment = HorizontalAlignment.Stretch;
                    item.HorizontalContentAlignment = HorizontalAlignment.Center;
                }
            bar.Visibility = segmented ? Visibility.Visible : Visibility.Collapsed;
            menu.Visibility = segmented ? Visibility.Collapsed : Visibility.Visible;
        }
        SizeChanged += (_, _) => Layout(); Loaded += (_, _) => { SetValue(currentValue); Layout(); };
    }
    private void Choose(int index)
    {
        if (syncing || index < 0 || index >= values.Count) return;
        if (currentValue is null) { SetValue(null); return; }
        var value = values[index].Value;
        SetValue(value); ValueChosen?.Invoke(value);
    }
    public void SetValue(JsonElement? value)
    {
        currentValue = value;
        syncing = true;
        try
        {
            var index = value is {} current ? values.FindIndex(v => JsonElement.DeepEquals(v.Value, current)) : -1;
            bar.SelectedItem = index >= 0 ? bar.Items[index] : null;
            menu.SelectedIndex = index;
        }
        finally { syncing = false; }
    }
}
