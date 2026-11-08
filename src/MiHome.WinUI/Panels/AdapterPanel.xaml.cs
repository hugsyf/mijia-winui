using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Panels;

public sealed partial class AdapterPanel : UserControl, IRefreshablePanel
{
    private readonly DeviceIdentity device = null!;
    private readonly DeviceAdapter adapter = null!;
    private readonly DeviceControlSession session = null!;
    private bool actionBusy;
    private readonly List<Action> updates = [];
    public event Action<DeviceCardState>? CardStateRead;
    public AdapterPanel() => InitializeComponent();
    public AdapterPanel(DeviceIdentity device, DeviceSchema schema, IDeviceService service, bool preview = false) : this()
    {
        this.device=device; adapter=device.Adapter!;
        session=new(device,schema,service);
        foreach(var widget in adapter.Drawer) DrawerWidgets.Children.Add(Build(widget));
        foreach(var widget in adapter.Full) advanced.Children.Add(Build(widget));
        session.StateRead += state=>CardStateRead?.Invoke(state);
        session.Changed += Update;
        Update();
    }
    public void SetExpanded(bool expanded) => advanced.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
    public Task RefreshAsync() => session.RefreshAsync();
    private FrameworkElement Build(AdapterWidget widget, List<AdapterCondition>? inherited=null)
    {
        widget=AdapterLocalizationResolver.Widget(adapter,widget);
        var conditions=new List<AdapterCondition>(inherited??[]);if(widget.EnabledWhen is {} enabled)conditions.Add(enabled);
        bool Enabled()=>AdapterConditions.Enabled(adapter,session.State,conditions);
        void RequireEnabled(){if(!Enabled())throw new BridgeException("condition",Infrastructure.UiText.Get("ConditionDisabled"));}
        FrameworkElement body;
        if(widget.Kind=="group")
        {
            var group=DeviceUi.Stack(DeviceUi.Text(widget.Label,"BodyStrongTextBlockStyle"));
            foreach(var child in widget.Children??[]) group.Children.Add(Build(child,conditions));
            body=group;
        }
        else if (widget.Kind == "readings")
            body = DeviceUi.Stack(DeviceUi.Text(widget.Label,"BodyStrongTextBlockStyle"), new ReadingsGrid((widget.Children ?? []).Select(child=>Build(child,conditions))));
        else
        {
            if (widget.Kind == "action")
            {
                var action = new ActionControl(device,adapter.Actions.Single(a=>a.Id==widget.Action),widget,session.Service);
                action.SetGuard(Enabled);
                action.BusyChanged += active => { actionBusy = active; Update(); };
                action.Executed += async () => { try { await session.RefreshAsync(); } catch (BridgeException e) { feedback.Message=e.Message;feedback.IsOpen=true;feedback.Visibility=Visibility.Visible; } };
                updates.Add(()=>action.SetAvailable(device.Online && !session.Busy && !actionBusy && Enabled()));
                body=action;
            }
            else
            {
            var binding=adapter.Bindings.Single(b=>b.Id==widget.Binding);
            var property=AdapterControlValues.WithLabels(binding.Property, widget.Options) with {Name=widget.Label};
            if(widget.Kind is "choice" or "select" or "brightness" or "colorTemperature" or "color" or "duration" or "slider" or "number" or "level" or "textInput")
            {
                AdapterValueEditor? editor = null;
                editor = new(widget, property, async value =>
                {
                    RequireEnabled();await session.WriteAsync(property, value);
                    if (session.Feedback is {} message) editor!.ShowError(message);
                });
                updates.Add(() => editor.SetValue(DeviceCardPresentation.TryValue(session.State, property, out var value) ? value : null,
                    !session.Busy && !actionBusy && Enabled() && DeviceExperience.CanWrite(device, session.State, property, null)));
                body = editor;
            }
            else if(widget.Kind is "toggle")
            {
                var row=new PropertyRow(property,async (editor,value)=>
                {
                    RequireEnabled();editor.BeginWrite(value);
                    await session.WriteAsync(property,value);
                    if(session.Feedback is {} message)editor.ShowError(message);
                }, label:widget.Label);
                updates.Add(()=>
                {
                    row.IsEnabled=!session.Busy && !actionBusy && Enabled() && DeviceExperience.CanWrite(device,session.State,property,null);
                    if(DeviceCardPresentation.TryValue(session.State,property,out var value))row.Update(value);else row.MarkStale("暂不可用");
                });
                body=row;
            }
            else if (widget.Kind == "indicator")
            {
                var indicator = new InfoBar { IsOpen = true, IsClosable = false, Title = widget.Label };
                updates.Add(() =>
                {
                    var valid = DeviceCardPresentation.TryValue(session.State, property, out var value);
                    var mapping = valid ? widget.States?.FirstOrDefault(s => JsonElement.DeepEquals(s.Value, value)) : null;
                    indicator.Message = valid ? mapping?.Label ?? AdapterReadings.Format(session.State, property) : "状态未知";
                    indicator.Severity = mapping?.Tone switch { "success" => InfoBarSeverity.Success, "warning" => InfoBarSeverity.Warning, "critical" => InfoBarSeverity.Error, _ => InfoBarSeverity.Informational };
                });
                body = indicator;
            }
            else if (widget.Kind == "text")
            {
                var text = DeviceUi.Text(MiHome.WinUI.Infrastructure.UiText.Get("Sbda050585a00")); text.IsTextSelectionEnabled = true; text.MaxLines = 6; text.TextTrimming = TextTrimming.CharacterEllipsis;
                var copy = new Button { Content = new FontIcon { Glyph = "\uE8C8", FontSize = 16 }, HorizontalAlignment = HorizontalAlignment.Right };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, "复制" + widget.Label); ToolTipService.SetToolTip(copy, "复制");
                var header = new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                header.Children.Add(DeviceUi.Secondary(widget.Label)); Grid.SetColumn(copy, 1); header.Children.Add(copy);
                string? complete = null;
                copy.Click += (_, _) =>
                {
                    if (complete is null) return;
                    try { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(complete); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }
                    catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                    { feedback.Message = "暂时无法复制。"; feedback.IsOpen = true; feedback.Visibility = Visibility.Visible; }
                };
                updates.Add(() =>
                {
                    complete = DeviceCardPresentation.TryValue(session.State, property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    text.Text = complete is null ? "—" : complete.Length > 4096 ? complete[..4096] + "…" : complete;
                    copy.IsEnabled = complete is not null;
                });
                body = DeviceUi.Surface(DeviceUi.Stack(header, text));
            }
            else
            {
                var value=DeviceUi.Text(MiHome.WinUI.Infrastructure.UiText.Get("Sbda050585a00"),"TitleTextBlockStyle");
                var content=DeviceUi.Stack(DeviceUi.Secondary(widget.Label),value);
                if (widget.Kind == "gauge")
                {
                    var ring = new ProgressRing { IsIndeterminate = false, Minimum = 0, Maximum = 100, Width = 64, Height = 64 };
                    var row = new Grid { ColumnSpacing = 16 };
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
                    content.Children.Remove(value); row.Children.Add(ring); Grid.SetColumn(value, 1); row.Children.Add(value); value.VerticalAlignment = VerticalAlignment.Center;
                    content.Children.Add(row);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ring, widget.Label);
                    updates.Add(() =>
                    {
                        var amount = DeviceCardPresentation.TryValue(session.State,property,out var v) && v.TryGetDouble(out var n) ? AdapterControlValues.Gauge(property,n) : null;
                        ring.Value = amount ?? 0; ring.Opacity = amount.HasValue ? 1 : .3;
                    });
                }
                if(widget.Kind=="progress")
                {
                    var progress=new ProgressBar {Minimum=property.Range[0],Maximum=property.Range[1]};
                    content.Children.Add(progress);
                    updates.Add(()=>progress.Value=DeviceCardPresentation.TryValue(session.State,property,out var v)&&v.TryGetDouble(out var number)?number:progress.Minimum);
                }
                updates.Add(()=>
                {
                    if (widget.Kind != "countdown") { value.Text=AdapterReadings.Format(session.State,property); return; }
                    var valid = DeviceCardPresentation.TryValue(session.State,property,out var reading) && reading.TryGetDouble(out _);
                    bool? active = null;
                    if (widget.ActiveBinding is {} activeId)
                    {
                        var activeProperty = adapter.Bindings.Single(b=>b.Id==activeId).Property;
                        if (DeviceCardPresentation.TryValue(session.State,activeProperty,out var enabled)) active=enabled.GetBoolean();
                    }
                    value.Text = active == false ? "未启用" : widget.ActiveBinding is not null && active is null ? "状态未知" : valid ? AdapterControlValues.Duration(reading.GetDouble(),property.Unit) : "—";
                });
                if (widget.Kind == "countdown") content.Children.Add(DeviceUi.Secondary("设备报告的剩余时间"));
                body=DeviceUi.Surface(content);
            }
            }
        }
        if(widget.When is {} condition)
            updates.Add(()=>
            {
                var p=adapter.Bindings.Single(b=>b.Id==condition.Binding).Property;
                body.Visibility=DeviceCardPresentation.TryValue(session.State,p,out var value)&&JsonElement.DeepEquals(value,condition.Value)?Visibility.Visible:Visibility.Collapsed;
            });
        if(conditions.Count>0)
        {
            var gate=new ConditionGate {Child=body};
            updates.Add(()=>{gate.IsEnabled=Enabled();gate.Visibility=body.Visibility;});
            return gate;
        }
        return body;
    }
    private void Update()
    {
        foreach(var update in updates)update();
        feedback.Message=session.Feedback??"";
        feedback.IsOpen=session.Feedback is not null;
        feedback.Visibility=feedback.IsOpen?Visibility.Visible:Visibility.Collapsed;
    }
}
