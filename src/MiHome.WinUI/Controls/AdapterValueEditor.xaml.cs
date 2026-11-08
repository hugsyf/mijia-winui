using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Panels;
namespace MiHome.WinUI.Controls;

// Edits stay local until submission. Every submission goes through DeviceControlSession.
public sealed partial class AdapterValueEditor : UserControl
{
    private readonly PropertySchema property = null!;
    private readonly Func<object?, Task> write = _ => Task.CompletedTask;
    private readonly List<Control> editors = [];
    private Action<JsonElement?> show = _ => { };
    private Func<object?> desired = () => null;
    private JsonElement? last;
    private object? pending;
    private bool updating, dirty, writable;
    private bool colorOpen;
    private readonly bool inlineActions;
    private readonly int maxLength;
    private int revision;
    public AdapterValueEditor() => InitializeComponent();
    public AdapterValueEditor(AdapterWidget widget, PropertySchema property, Func<object?, Task> write, bool surface = true) : this()
    {
        property = property with { Name = widget.Label };
        this.property = property; this.write = write;
        maxLength = widget.MaxLength;
        inlineActions = widget.Kind is not ("choice" or "select" or "level" or "color");
        HeadingLabel.Text = Infrastructure.UiText.State(widget.Label);
        if (widget.Kind is "choice" or "select")
        {
            var choice = new EnumChoiceControl(property.Values, widget.Presentation);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, widget.Label);
            body.Children.Add(choice); editors.Add(choice);
            choice.ValueChosen += async value => { if (!updating && writable) await Submit(value); };
            show = value => choice.SetValue(value);
        }
        else if (widget.Kind == "color") BuildColor();
        else if (widget.Kind == "duration") BuildDuration();
        else if (widget.Kind == "number") BuildNumber();
        else if (widget.Kind == "level") BuildLevel(widget);
        else if (widget.Kind == "textInput") BuildTextInput();
        else BuildSlider(widget.Kind == "colorTemperature");
        error.Visibility = Visibility.Collapsed;
        apply.Click += async (_, _) => await Submit(desired());
        cancel.Click += (_, _) => { dirty = false; pending = null; SetValue(last, writable); };
        if (!surface) { EditorSurface.Padding = new Thickness(0); EditorSurface.BorderThickness = new Thickness(0); EditorSurface.Background = null; }
    }
    private void Changed()
    {
        if (updating) return;
        revision++; dirty = true; pending = null; actions.Visibility = inlineActions ? Visibility.Visible : Visibility.Collapsed;
    }
    private void BuildSlider(bool temperature)
    {
        var step = property.Range.Length > 2 && property.Range[2] > 0 ? property.Range[2] : 1;
        var slider = new Slider { Minimum = property.Range[0], Maximum = property.Range[1], StepFrequency = step };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, property.Name);
        if (temperature)
        {
            var gradient = new LinearGradientBrush { StartPoint = new(0, .5), EndPoint = new(1, .5),
                GradientStops = { new() { Color = Colors.Orange, Offset = 0 }, new() { Color = Colors.White, Offset = .5 }, new() { Color = Colors.LightSkyBlue, Offset = 1 } } };
            var accessibility = new Windows.UI.ViewManagement.AccessibilitySettings();
            if (!accessibility.HighContrast)
                foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                {
                    slider.Resources["SliderTrackFill" + suffix] = gradient;
                    slider.Resources["SliderTrackValueFill" + suffix] = new SolidColorBrush(Colors.Transparent);
                }
        }
        headingValue.Visibility = Visibility.Visible;
        slider.ValueChanged += (_, _) => { if (updating) return; headingValue.Text = Format(slider.Value); Changed(); };
        body.Children.Add(slider); editors.Add(slider);
        desired = () => slider.Value;
        show = value =>
        {
            if (value is {} v && v.TryGetDouble(out var n)) { slider.Value = n; headingValue.Text = Format(n); }
            else headingValue.Text = MiHome.WinUI.Infrastructure.UiText.Get("Sbda050585a00");
        };
    }
    private string Format(double value) => value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
        + (property.Unit == "percentage" ? "%" : PropertyPresentation.Unit(property.Unit).Length > 0 ? " " + PropertyPresentation.Unit(property.Unit) : "");
    private void BuildNumber()
    {
        headingValue.Text = PropertyPresentation.Unit(property.Unit);
        headingValue.Visibility = headingValue.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var input = new NumberBox { Value = double.NaN, Minimum = property.Range[0], Maximum = property.Range[1],
            SmallChange = property.Range.Length > 2 ? property.Range[2] : 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, property.Name);
        input.ValueChanged += (_, _) => Changed(); body.Children.Add(input); editors.Add(input);
        desired = () => input.Value;
        show = value => input.Value = value is {} v && v.TryGetDouble(out var n) ? n : double.NaN;
    }
    private void BuildTextInput()
    {
        var input = new TextBox { MaxLength = maxLength, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, property.Name);
        input.TextChanged += (_, _) => Changed(); body.Children.Add(input); editors.Add(input);
        desired = () => input.Text;
        show = value => input.Text = value is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? "" : "";
    }
    private void BuildLevel(AdapterWidget widget)
    {
        var values = AdapterControlValues.Levels(property).Select(n => new EnumValue(
            property.Type.StartsWith("int") || property.Type.StartsWith("uint") ? JsonSerializer.SerializeToElement((long)n) : JsonSerializer.SerializeToElement(n), Format(n))).ToList();
        var choice = new EnumChoiceControl(values, widget.Presentation);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, widget.Label);
        body.Children.Add(choice); editors.Add(choice);
        choice.ValueChosen += async value => { if (!updating && writable) await Submit(value); };
        show = value => choice.SetValue(value);
    }

    private void BuildColor()
    {
        var view = new ColorEditorView();
        var picker=view.Picker;var swatch=view.Swatch;var button=view.ColorButton;var flyout=view.ColorFlyout;
        var dismiss=view.CancelButton;var confirm=view.ApplyButton;
        heading.Visibility = Visibility.Collapsed;
        dismiss.Click += (_, _) => flyout.Hide();
        flyout.Opening += (_, _) => { updating = true; show(last); updating = false; };
        flyout.Opened += (_, _) => colorOpen = true;
        flyout.Closed += (_, _) => { colorOpen = false; show(last); };
        confirm.Click += async (_, _) => { await Submit(AdapterControlValues.Rgb(picker.Color.R, picker.Color.G, picker.Color.B)); flyout.Hide(); };
        body.Children.Add(view); editors.Add(button);
        show = value => { if (value is {} v && v.TryGetInt32(out var rgb)) { picker.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); swatch.Background = new SolidColorBrush(picker.Color); } };
    }
    private void BuildDuration()
    {
        var row = new Grid { ColumnSpacing = 8 };
        NumberBox Part(string name, double max)
        {
            var input = new NumberBox { Header = name, Minimum = 0, Maximum = max, SmallChange = 1, MinWidth = 64, HorizontalAlignment = HorizontalAlignment.Stretch, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            Grid.SetColumn(input, row.ColumnDefinitions.Count); row.ColumnDefinitions.Add(new());
            input.ValueChanged += (_, _) => Changed(); row.Children.Add(input); editors.Add(input); return input;
        }
        var scale = AdapterControlValues.SecondsPerUnit(property.Unit);
        var stepSeconds = (property.Range.Length > 2 ? property.Range[2] : 1) * scale;
        var originSeconds = property.Range[0] * scale;
        var hours = property.Range[1] * scale >= 3600 ? Part("时", Math.Floor(property.Range[1] * scale / 3600)) : null;
        var minutes = hours is null || stepSeconds % 3600 != 0 || originSeconds % 3600 != 0 ? Part("分", 59) : null;
        var seconds = stepSeconds % 60 != 0 || originSeconds % 60 != 0 ? Part("秒", 59) : null;
        body.Children.Add(row);
        desired = () => ((hours?.Value ?? 0) * 3600 + (minutes?.Value ?? 0) * 60 + (seconds?.Value ?? 0)) / scale;
        show = value =>
        {
            var n = value is {} v && v.TryGetDouble(out var d) ? d * AdapterControlValues.SecondsPerUnit(property.Unit) : double.NaN;
            if (hours is not null) hours.Value = double.IsFinite(n) ? Math.Floor(n / 3600) : double.NaN;
            if (minutes is not null) minutes.Value = double.IsFinite(n) ? Math.Floor(n / 60) % 60 : double.NaN;
            if (seconds is not null) seconds.Value = double.IsFinite(n) ? n % 60 : double.NaN;
        };
    }
    private async Task Submit(object? value)
    {
        if (!writable || value is null) return;
        if (value is string text && (text.Length > maxLength || text.Any(char.IsControl)))
        { ShowError("文本超出允许长度或包含控制字符。"); return; }
        if (value is double number && !AdapterControlValues.IsValid(property, number) || value is int rgb && !AdapterControlValues.IsValid(property, rgb))
        { ShowError("数值不符合设备允许的范围或步长。"); return; }
        if (value is double integer && (property.Type.StartsWith("int") || property.Type.StartsWith("uint"))) value = checked((long)integer);
        var submittedRevision = revision;
        pending = value; dirty = true;
        try
        {
            await write(value);
            if (revision == submittedRevision && last is {} current && JsonElement.DeepEquals(current, JsonSerializer.SerializeToElement(value)))
            { dirty = false; pending = null; SetValue(last, writable); }
        }
        catch (BridgeException e) { ShowError(e.Message); }
    }
    public void ShowError(string message) { error.Text = message; error.Visibility = Visibility.Visible; }
    public void SetValue(JsonElement? value, bool enabled)
    {
        last = value; writable = enabled;
        if (pending is not null && value is {} v && JsonElement.DeepEquals(v, JsonSerializer.SerializeToElement(pending))) { pending = null; dirty = false; }
        updating = true;
        try { if (!dirty && !colorOpen) show(value); }
        finally { updating = false; }
        foreach (var editor in editors) editor.IsEnabled = enabled;
        apply.IsEnabled = enabled; cancel.IsEnabled = enabled;
        actual.Text = value is {} current ? "当前：" + (property.Unit is "seconds" or "minutes" or "hours" && current.TryGetDouble(out var n)
            ? AdapterControlValues.Duration(n, property.Unit) : current.ToString() + (property.Unit == "kelvin" ? " K" : property.Unit == "percentage" ? " %" : "")) : "暂不可用";
        actual.Visibility = value is null || dirty && inlineActions ? Visibility.Visible : Visibility.Collapsed;
        actions.Visibility = dirty && inlineActions ? Visibility.Visible : Visibility.Collapsed;
        if (!dirty) error.Visibility = Visibility.Collapsed;
    }
}
