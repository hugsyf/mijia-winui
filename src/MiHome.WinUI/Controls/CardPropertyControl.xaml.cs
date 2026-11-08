using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Panels;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed partial class CardPropertyControl : UserControl
{
    private readonly ExpandedCardItem item = null!;
    private readonly Func<PropertySchema,object?,Task<string?>> write = (_, _) => Task.FromResult<string?>(null);
    private readonly Action<string> message = _ => { };
    private readonly DispatcherQueueTimer debounce = null!;
    private PropertySchema property = null!;
    private DeviceTile? tile;
    private Slider? slider;
    private ComboBox? choice;
    private ToggleSwitch? toggle;
    private AdapterValueEditor? duration;
    private Button? switchButton, durationButton;
    private bool lastBusy;
    private bool updating, dirty, dragging, keyboardHeld, sending, active = true;
    private int alternative;
    public CardPropertyControl() => InitializeComponent();
    internal CardPropertyControl(ExpandedCardItem item, Func<PropertySchema,object?,Task<string?>> write, Action<string> message) : this()
    {
        this.item=item; this.write=write; this.message=message; property=item.Property!;
        debounce=DispatcherQueue.CreateTimer(); debounce.Interval=TimeSpan.FromMilliseconds(350);
        debounce.IsRepeating=false; debounce.Tick+=async(_,_)=>await Submit();
        Unloaded+=(_,_)=>{active=false;debounce.Stop();}; Loaded+=(_,_)=>active=true;
        if(item.Widget.Kind is "slider" or "brightness" or "colorTemperature")
        {
            CreateSlider();
            if(item.Alternatives is {Count:>1})
            {
                var mode=ModeSwitch;mode.Visibility=Visibility.Visible;switchButton=mode;
                mode.Click+=(_,e)=>{debounce.Stop();dirty=false;property=item.Alternatives[++alternative%item.Alternatives.Count];CreateSlider();if(tile is not null)Update(tile,lastBusy);};
            }
        }
        else if(item.Widget.Kind is "choice" or "select" or "level")
        {
            value.Visibility=Visibility.Collapsed;
            var options=property.Values.Count>0?property.Values:AdapterControlValues.Levels(property).Select(n=>new EnumValue(JsonSerializer.SerializeToElement((long)n),n.ToString("0.##"))).ToList();
            choice=new ComboBox {ItemsSource=options,DisplayMemberPath="DisplayLabel",SelectedIndex=-1,HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice,item.Widget.Label);
            choice.SelectionChanged+=async(_,_)=>{if(!updating&&choice.SelectedItem is EnumValue option){dirty=false;await Send(option.Value);}};body.Children.Add(choice);
        }
        else if(item.Widget.Kind=="toggle")
        {
            toggle=new ToggleSwitch {OnContent=null,OffContent=null,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Right};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,item.Widget.Label);
            toggle.Toggled+=async(_,_)=>{if(!updating)await Send(toggle.IsOn);};body.Children.Add(toggle);
        }
        else if(item.Widget.Kind=="duration")
        {
            value.Visibility=Visibility.Collapsed;
            duration=new(item.Widget,property,async desired=>{var error=await Send(desired);if(error is not null)duration!.ShowError(error);},surface:false);
            var button=new Button {Content="设置…",HorizontalAlignment=HorizontalAlignment.Stretch,Flyout=new Flyout {Content=duration}};
            durationButton=button;
            body.Children.Add(button);
        }
        else { value.FontSize=22; Grid.SetColumn(value,0); Grid.SetRow(value,1); header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetColumnSpan(value,3); }
        Tapped+=(_,e)=>e.Handled=true;DoubleTapped+=(_,e)=>e.Handled=true;
    }
    private void CreateSlider()
    {
            // Track brushes are resolved when the native template is created.
            // Recreate only on an explicit brightness/temperature switch, never on a reading update.
            if(slider is not null)body.Children.Remove(slider);
            slider=new Slider { IsEnabled=false, Margin=new Thickness(0,-4,0,0) };
            ConfigureSlider(); body.Children.Add(slider);
            slider.ValueChanged+=(_,_)=>
            {
                if(updating)return;
                dirty=true;value.Text=Number(slider.Value);
                if(!dragging&&!keyboardHeld){debounce.Stop();debounce.Start();}
            };
            slider.AddHandler(PointerPressedEvent,new PointerEventHandler((_,e)=>{slider.Focus(FocusState.Pointer);dragging=true;debounce.Stop();e.Handled=true;}),true);
            slider.AddHandler(PointerReleasedEvent,new PointerEventHandler(async(_,e)=>{dragging=false;e.Handled=true;await Submit();}),true);
            slider.AddHandler(PointerCanceledEvent,new PointerEventHandler((_,e)=>{dragging=false;dirty=false;debounce.Stop();Paint();e.Handled=true;}),true);
            slider.AddHandler(KeyDownEvent,new KeyEventHandler((_,e)=>{if(AdjustmentKey(e.Key)){keyboardHeld=true;debounce.Stop();e.Handled=true;}}),true);
            slider.AddHandler(KeyUpEvent,new KeyEventHandler(async(_,e)=>{if(AdjustmentKey(e.Key)){keyboardHeld=false;e.Handled=true;await Submit();}}),true);
    }
    private static bool AdjustmentKey(Windows.System.VirtualKey key)=>key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End;
    private void ConfigureSlider()
    {
        if(slider is null)return;
        updating=true;
        slider.Minimum=property.Range[0];slider.Maximum=property.Range[1];slider.StepFrequency=property.Range.Length>2?property.Range[2]:1;
        var temperature=DeviceCardPresentation.Semantic(property.PropertyType)=="color-temperature" && !new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        foreach(var suffix in new[]{"","PointerOver","Pressed"})
        {
            slider.Resources.Remove("SliderTrackFill"+suffix);slider.Resources.Remove("SliderTrackValueFill"+suffix);
            if(temperature)
            {
                slider.Resources["SliderTrackFill"+suffix]=new Microsoft.UI.Xaml.Media.LinearGradientBrush {StartPoint=new(0,.5),EndPoint=new(1,.5),GradientStops={new(){Color=Microsoft.UI.Colors.Orange,Offset=0},new(){Color=Microsoft.UI.Colors.White,Offset=.5},new(){Color=Microsoft.UI.Colors.LightSkyBlue,Offset=1}}};
                slider.Resources["SliderTrackValueFill"+suffix]=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        label.Text=DeviceCardPresentation.Semantic(property.PropertyType)=="brightness"?"亮度":DeviceCardPresentation.Semantic(property.PropertyType)=="color-temperature"?"色温":item.Widget.Label;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,label.Text);
        updating=false;
    }
    private string Number(double n)=>n.ToString("0.##",System.Globalization.CultureInfo.CurrentCulture)+(property.Unit=="percentage"?"%":property.Unit=="kelvin"?" K":" "+PropertyPresentation.Unit(property.Unit));
    public void Update(DeviceTile next,bool busy)
    {
        tile=next;
        lastBusy=busy;
        var enabled=!next.IsEditing&&!busy&&!sending&&DeviceExperience.CanWrite(next.Device,next.State,property,null);
        if(slider is not null)slider.IsEnabled=enabled;
        if(choice is not null)choice.IsEnabled=enabled;
        if(toggle is not null)toggle.IsEnabled=enabled;
        if(durationButton is not null)durationButton.IsEnabled=enabled;
        if(switchButton is not null)switchButton.IsEnabled=!next.IsEditing&&!busy&&!sending;
        duration?.SetValue(DeviceCardPresentation.TryValue(next.State,property,out var v)?v:null,enabled);
        Paint();
    }
    private void Paint()
    {
        if(tile is null||dirty||dragging||keyboardHeld||sending)return;
        updating=true;
        try
        {
            if(slider is null)label.Text=Infrastructure.UiText.State(item.Widget.Label);
            var valid=DeviceCardPresentation.TryValue(tile.State,property,out var current);
            if(slider is not null) {if(valid&&current.TryGetDouble(out var n)) {slider.Value=n;value.Text=Number(n);}else value.Text=MiHome.WinUI.Infrastructure.UiText.Get("Sbda050585a00");}
            else if(choice is not null)choice.SelectedItem=valid?((IEnumerable<EnumValue>)choice.ItemsSource).FirstOrDefault(v=>JsonElement.DeepEquals(v.Value,current)):null;
            else if(toggle is not null){if(valid)toggle.IsOn=current.GetBoolean();value.Text=valid?(current.GetBoolean()?"已开启":"已关闭"):"状态未知";}
            else if(item.Widget.Kind=="countdown")
            {
                bool? enabled=null;
                if(item.Widget.ActiveBinding is {} id && tile.Device.Adapter is {} adapter)
                {
                    var activeProperty=adapter.Bindings.Single(b=>b.Id==id).Property;
                    if(DeviceCardPresentation.TryValue(tile.State,activeProperty,out var activeValue))enabled=activeValue.GetBoolean();
                }
                value.Text=enabled==false?"未启用":item.Widget.ActiveBinding is not null && enabled is null?"状态未知":valid&&current.TryGetDouble(out var n)?AdapterControlValues.Duration(n,property.Unit):"—";
            }
            else if(DeviceCardPresentation.Semantic(property.ServiceType)=="router" && DeviceCardPresentation.Semantic(property.PropertyType) is "download-speed" or "upload-speed")value.Text=DeviceCardPresentation.RouterSpeed(tile.State,DeviceCardPresentation.Semantic(property.PropertyType))??"—";
            else value.Text=AdapterReadings.Format(tile.State,property);
        }
        finally{updating=false;}
    }
    private async Task Submit()
    {
        debounce.Stop();if(!dirty||slider is null||sending||!active)return;
        dirty=false;var n=slider.Value;
        if(!AdapterControlValues.IsValid(property,n)){Paint();return;}
        object number=property.Type.StartsWith("int")||property.Type.StartsWith("uint")?(object)checked((long)n):n;
        await Send(number);
    }
    private async Task<string?> Send(object? desired)
    {
        if(tile is null||tile.IsEditing||sending||!active)return "当前不可操作。";
        var focused=XamlRoot is null?null:FocusManager.GetFocusedElement(XamlRoot) as Control;
        var restore=focused is not null&&(focused==slider||focused==choice||focused==toggle);
        var focusState=focused?.FocusState??FocusState.Programmatic;
        sending=true;
        try {var error=await write(property,desired);if(error is not null)message(error);return error;}
        finally
        {
            sending=false;if(tile is not null)Update(tile,lastBusy);
            if(restore&&focused is {IsEnabled:true}&&active&&XamlRoot is not null&&FocusManager.GetFocusedElement(XamlRoot) is not Control)
                focused.Focus(focusState==FocusState.Unfocused?FocusState.Programmatic:focusState);
        }
    }
}
