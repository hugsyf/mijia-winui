using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Controls;
namespace MiHome.WinUI.Panels;

public sealed partial class PropertyRow : UserControl
{
    public PropertySchema Schema { get; } = null!;
    public JsonElement? LastValue { get; private set; }
    private readonly Func<PropertyRow, object?, Task> write = (_, _) => Task.CompletedTask;
    private Control? editor;
    private Button? resetInput;
    private StackPanel? applyActions;
    private bool changing, dirty;
    private bool writing;
    private string lastDisplay = "尚未读取";
    private JsonElement? pendingValue;
    private int editRevision, pendingRevision;
    public bool HasPendingWrite => pendingValue is not null;
    public PropertyRow() => InitializeComponent();
    public PropertyRow(PropertySchema schema, Func<PropertyRow, object?, Task> write, bool surface = true, string? label = null) : this()
    {
        Schema = schema;
        this.write = write;
        PropertyLabel.Text = label ?? PropertyPresentation.Name(schema);
        if (schema.Writable) BuildEditor();
        else if (!schema.Readable) ShowError("此属性没有可用的读写权限。");
        if (editor is ToggleSwitch toggle)
        {
            Grid.SetColumn(current,0); Grid.SetRow(current,1); heading.RowSpacing=4;
            ToggleHost.Content=toggle;EditorActions.Visibility=Visibility.Collapsed;
            editors.Visibility = Visibility.Collapsed;
        }
        if (!surface) { RowSurface.Padding=new Thickness(0); RowSurface.BorderThickness=new Thickness(0); RowSurface.Background=null; }
    }
    private void BuildEditor()
    {
        if (Schema.Values.Count > 0)
        {
            var combo = new ComboBox { ItemsSource = Schema.Values, DisplayMemberPath = "DisplayLabel", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "选择值" };
            combo.SelectionChanged += (_, _) => { if (!changing) Edited(); };
            editor = combo;
        }
        else if (Schema.Type == "bool")
        {
            var toggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !Schema.Readable };
            toggle.Toggled += async (_, _) => { if (!changing) { Edited(); await SubmitAsync(); } };
            editor = toggle;
        }
        else if (Schema.Type.StartsWith("int") || Schema.Type.StartsWith("uint") || Schema.Type is "float" or "double")
        {
            var number = new NumberBox { Value = double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (Schema.Range.Length >= 2) { number.Minimum = Schema.Range[0]; number.Maximum = Schema.Range[1]; }
            if (Schema.Range.Length >= 3) number.SmallChange = Schema.Range[2];
            number.ValueChanged += (_, _) =>
            {
                if (!changing) Edited();
            };
            number.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => { if (!changing) Edited(); });
            editor = number;
        }
        else if (Schema.Type == "string")
        {
            var text = new TextBox();
            text.TextChanged += (_, _) => { if (!changing) Edited(); };
            editor = text;
        }
        if (editor is null) { ShowError("暂不支持编辑此属性类型。"); return; }
        AutomationProperties.SetName(editor, PropertyPresentation.Name(Schema));
        if (Schema.Range.Length >= 2) ToolTipService.SetToolTip(editor, $"范围 {Schema.Range[0]}–{Schema.Range[1]}，步长 {(Schema.Range.Length > 2 ? Schema.Range[2] : 1)}");
        var actions = EditorActions; actions.Visibility = Schema.Readable ? Visibility.Collapsed : Visibility.Visible;
        applyActions = actions;
        if (editor is not ToggleSwitch)
        {
            var apply = ApplyValue;
            AutomationProperties.SetName(apply, "应用 " + PropertyPresentation.Name(Schema));
            apply.Click += async (_, _) => await SubmitAsync();
        }
        else ApplyValue.Visibility = Visibility.Collapsed;
        if (Schema.Readable)
        {
            var reset = ResetValue;
            resetInput = reset;
            ToolTipService.SetToolTip(reset, "恢复为最近读取的值；不向设备发送指令。");
            AutomationProperties.SetName(reset, "恢复 " + PropertyPresentation.Name(Schema) + " 的已读取值");
            reset.Click += (_, _) =>
            {
                if (LastValue is not { } value) { ShowError("请先刷新状态。"); return; }
                dirty = false;
                pendingValue = null;
                feedback.Visibility = Visibility.Collapsed;
                Update(value);
            };
        }
        if (editor is not ToggleSwitch)
        {
            editors.Children.Add(editor);
        }
        if (!Schema.Readable) current.Text = MiHome.WinUI.Infrastructure.UiText.Get("S30a0132ed5d2");
    }
    private async Task SubmitAsync()
    {
        if (writing) return;
        try
        {
            object? value = editor switch
            {
                ToggleSwitch t => t.IsOn,
                ComboBox c when c.SelectedItem is EnumValue e => e.Value,
                NumberBox n when double.IsFinite(n.Value) => Schema.Type.StartsWith("int") || Schema.Type.StartsWith("uint")
                    ? n.Value == Math.Truncate(n.Value) ? (object)checked((long)n.Value) : throw new Exception("请输入整数。") : n.Value,
                TextBox t => t.Text,
                _ => throw new Exception("请输入或选择有效值。")
            };
            if (editor is NumberBox number && Schema.Range.Length >= 2)
            {
                var v = number.Value;
                if (v < Schema.Range[0] || v > Schema.Range[1]) throw new Exception("数值超出允许范围。");
                if (Schema.Range.Length > 2 && Schema.Range[2] > 0)
                { var steps = (v - Schema.Range[0]) / Schema.Range[2]; if (Math.Abs(steps - Math.Round(steps)) > 0.000001) throw new Exception("数值不符合步长。"); }
            }
            await write(this, value);
        }
        catch (Exception e) { ShowError(e.Message); }
    }
    public void Update(JsonElement value)
    {
        LastValue = value;
        lastDisplay = PropertyPresentation.Value(Schema, value);
        current.Text = lastDisplay;
        if (pendingValue is { } expected && JsonElement.DeepEquals(expected, value))
        {
            pendingValue = null;
            if (editRevision == pendingRevision) dirty = false;
            ShowError(dirty ? "上次操作已确认；当前输入尚未提交。" : "设备状态已确认。");
        }
        current.Text = dirty ? "当前：" + lastDisplay : lastDisplay;
        current.Visibility = Schema.Type != "bool" && Schema.Writable && !dirty ? Visibility.Collapsed : Visibility.Visible;
        if (resetInput is not null) resetInput.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        if (applyActions is not null && editor is not ToggleSwitch) applyActions.Visibility = dirty || !Schema.Readable ? Visibility.Visible : Visibility.Collapsed;
        if (dirty || editor is null) return;
        changing = true;
        try
        {
            switch (editor)
            {
                case ToggleSwitch toggle when value.ValueKind is JsonValueKind.True or JsonValueKind.False: toggle.IsOn = value.GetBoolean(); toggle.IsEnabled = true; break;
                case NumberBox number when value.ValueKind == JsonValueKind.Number:
                    number.Value = value.GetDouble();
                    break;
                case TextBox text when value.ValueKind == JsonValueKind.String: text.Text = value.GetString() ?? ""; break;
                case ComboBox combo: combo.SelectedItem = Schema.Values.FirstOrDefault(v => JsonElement.DeepEquals(v.Value, value)); break;
            }
        }
        finally { changing = false; }
    }
    public void MarkStale(string reason = "可能已过期")
    {
        LastValue = null;
        current.Text = lastDisplay + " · " + reason;
        current.Visibility = Visibility.Visible;
        if (editor is ToggleSwitch toggle && Schema.Readable) toggle.IsEnabled = false;
    }
    public void ShowError(string message) { feedback.Text = message; feedback.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible; }
    public void SetWriting(bool value) { writing = value; IsEnabled = !value; }
    private void Edited()
    {
        dirty = true;
        if (applyActions is not null && editor is not ToggleSwitch) applyActions.Visibility = Visibility.Visible;
        editRevision++;
        if (resetInput is not null) resetInput.Visibility = Visibility.Visible;
        if (LastValue is not null) { current.Text = "当前：" + lastDisplay; current.Visibility = Visibility.Visible; }
    }
    public void BeginWrite(object? value) { pendingValue = JsonSerializer.SerializeToElement(value); pendingRevision = editRevision; ShowError("正在提交…"); }
    public void RejectWrite(string message) { pendingValue = null; ShowError(message); }
    public void AcceptWriteOnly() { pendingValue = null; dirty = false; ShowError("请求已接受；此属性只写，无法读取确认。"); }
}
