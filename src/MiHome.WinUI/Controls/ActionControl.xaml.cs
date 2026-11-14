using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Panels;
namespace MiHome.WinUI.Controls;

internal interface IEmbeddedActionPreview { bool EmbeddedPreview { get; } }

public sealed partial class ActionControl : UserControl
{
    private static readonly Dictionary<string, SemaphoreSlim> gates = [];
    internal static SemaphoreSlim DeviceGate(string id){if(!gates.TryGetValue(id,out var gate))gates[id]=gate=new(1);return gate;}
    private bool busy, unknown, available;
    private Func<bool> guard=()=>true;
    public void SetGuard(Func<bool> value)=>guard=value;
    public event Action<bool>? BusyChanged;
    public event Action? Executed;
    public ActionControl() => InitializeComponent();
    public ActionControl(DeviceIdentity device, AdapterAction action, AdapterWidget widget, IDeviceService service, bool compact = false) : this()
    {
        button.Content = widget.Label;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, widget.Label + "，执行前确认");
        feedback.Visibility = Visibility.Collapsed;
        feedback.MaxLines = compact ? 2 : 6;
        if (compact) { ActionSurface.Padding = new Thickness(0); ActionSurface.BorderThickness = new Thickness(0); ActionSurface.Background = null; }
        available = device.Online && service is IDeviceActionService;
        SetAvailable(available);
        button.Click += async (_, _) =>
        {
            if (!guard() || !available || busy || unknown || service is not IDeviceActionService actions || device.Adapter is null) return;
            var gate=DeviceGate(device.Id);
            if (!await gate.WaitAsync(0)) { Message("此设备正在执行另一项操作。"); return; }
            busy = true; SetAvailable(available); BusyChanged?.Invoke(true);
            feedback.Visibility=Visibility.Collapsed;
            try
            {
                var inputs = new List<Func<object?>>();
                var form = new ActionConfirmationView();form.SetIdentity(device.Name,string.IsNullOrEmpty(action.Action.DisplayName) ? action.Action.Name : action.Action.DisplayName);
                var invalid = form.ErrorLabel;
                if (widget.Arguments is {} presets)
                {
                    for (var i=0;i<presets.Count;i++)
                    {
                        var value = presets[i].Clone(); inputs.Add(()=>value);
                        form.Inputs.Children.Add(DeviceUi.Text(PropertyPresentation.Name(action.Action.Inputs[i])+"："+PropertyPresentation.Value(action.Action.Inputs[i],value)));
                    }
                }
                else foreach (var property in action.Action.Inputs) form.Inputs.Children.Add(Input(property,inputs));
                List<object?>? values = null;
                bool Validate()
                {
                    try { values = inputs.Select(read=>read()).ToList(); return true; }
                    catch (ArgumentException ex) { invalid.Text=ex.Message;invalid.Visibility=Visibility.Visible;return false; }
                }
                if (actions.IsSimulation && (NestedDialog() || service is IEmbeddedActionPreview {EmbeddedPreview:true}))
                {
                    // Import preview already runs inside a ContentDialog. Use a native flyout there.
                    var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    form.ShowCommands();var execute=form.ExecuteButton;var cancel=form.CancelButton;
                    var popup=new Flyout {Content=DeviceUi.Scroll(form,350)};
                    execute.Click+=(_,_)=>{if(Validate()){completion.TrySetResult(true);popup.Hide();}};
                    cancel.Click+=(_,_)=>popup.Hide();popup.Closed+=(_,_)=>completion.TrySetResult(false);
                    popup.ShowAt(button);
                    if(!await completion.Task || values is null)return;
                }
                else
                {
                    var dialog = new Dialogs.MessageDialog { XamlRoot = XamlRoot, Title = widget.Label, PrimaryButtonText = "执行" };dialog.SetBody(form,400);
                    dialog.PrimaryButtonClick+=(_,e)=>e.Cancel=!Validate();
                    if(await dialog.ShowAsync()!=ContentDialogResult.Primary || values is null)return;
                }
                if(!guard()){Message(Infrastructure.UiText.Get("ConditionDisabled"));return;}
                Message("正在提交…");
                var result = await actions.InvokeActionAsync(new(device.Id,device.Adapter.Id,action.Id,values,Guid.NewGuid().ToString(),true));
                Message(result.Message);
                if (result.Outputs.Count>0)
                    Message(result.Message+"\n"+string.Join("\n",result.Outputs.Select((v,i)=>PropertyPresentation.Name(action.Action.Outputs[i])+"："+PropertyPresentation.Value(action.Action.Outputs[i],v))));
                Executed?.Invoke();
            }
            catch (BridgeException e) { unknown = e.Code == "actionUnknown"; Message(e.Message); }
            catch (Exception) { Message("操作未完成；若已提交，请先检查设备状态。"); unknown = true; }
            finally { busy = false; gate.Release(); SetAvailable(available); BusyChanged?.Invoke(false); }
        };
    }
    private bool NestedDialog()
    {
        for(DependencyObject? parent=this;parent is not null;parent=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
            if(parent is ContentDialog)return true;
        return false;
    }
    public void SetAvailable(bool enabled) { available = enabled; button.IsEnabled = enabled && !busy && !unknown; }
    private void Message(string text) { feedback.Text = text; feedback.Visibility = Visibility.Visible; ToolTipService.SetToolTip(feedback,text); }
    private static FrameworkElement Input(PropertySchema property, List<Func<object?>> values)
    {
        var unit=PropertyPresentation.Unit(property.Unit);
        var label = PropertyPresentation.Name(property)+(unit.Length>0?"（"+unit+"）":"");
        if (property.Values.Count>0)
        {
            var combo = new ComboBox { Header = label, ItemsSource=property.Values, DisplayMemberPath="DisplayLabel", SelectedIndex=-1, HorizontalAlignment=HorizontalAlignment.Stretch };
            values.Add(()=>combo.SelectedItem is EnumValue value ? value.Value : throw new ArgumentException("请选择"+label+"。")); return combo;
        }
        if (property.Type=="bool")
        {
            var choice = new ComboBox {Header=label,ItemsSource=new[]{new EnumValue(JsonSerializer.SerializeToElement(true),"是"),new EnumValue(JsonSerializer.SerializeToElement(false),"否")},DisplayMemberPath="DisplayLabel",SelectedIndex=-1,PlaceholderText="请选择",HorizontalAlignment=HorizontalAlignment.Stretch};
            values.Add(()=>choice.SelectedItem is EnumValue value ? value.Value : throw new ArgumentException("请明确选择"+label+"。")); return choice;
        }
        if (property.Type=="string")
        {
            var input = new TextBox { Header=label, MaxLength=4096 };
            values.Add(()=>input.Text); return input;
        }
        var number = new NumberBox { Header=label, Value=double.NaN, SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
        if (property.Range.Length>=2) { number.Minimum=property.Range[0]; number.Maximum=property.Range[1]; }
        if (property.Range.Length>2) number.SmallChange=property.Range[2];
        values.Add(()=>
        {
            var n = number.Value;
            if (!double.IsFinite(n)) throw new ArgumentException("请输入"+label+"。");
            if (property.Range.Length>=2 && !Devices.AdapterControlValues.IsValid(property,n)) throw new ArgumentException(label+"不符合实际范围或步长。");
            if (property.Type.StartsWith("int") || property.Type.StartsWith("uint"))
            { if (n!=Math.Truncate(n) || Math.Abs(n)>9007199254740991d) throw new ArgumentException(label+"需要可精确表示的整数。"); return (object)(long)n; }
            return n;
        }); return number;
    }
}
