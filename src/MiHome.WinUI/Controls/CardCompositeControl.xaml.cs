using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed partial class CardCompositeControl : UserControl
{
    private AdapterWidget widget=null!;
    private DeviceTile tile=null!;
    private IDeviceService service=null!;
    private Func<PropertySchema,object?,Task<string?>> write=null!;
    private Func<Task> refresh=null!;
    private Action<string> feedback=null!;
    private Action<bool> actionBusy=null!;
    private readonly List<CompositeCardRow> rows=[];
    private readonly List<(AdapterWidget Widget,CardPropertyControl Control)> editors=[];
    private PropertySchema? colorProperty;
    private AdapterWidget? modeWidget,readingWidget;
    private EnumChoiceControl? modes;
    private bool busy,sending,unknown,active=true,colorOpen;
    private int paletteCount=-1;
    public CardCompositeControl()=>InitializeComponent();
    internal CardCompositeControl(AdapterWidget widget,DeviceTile tile,IDeviceService service,
        Func<PropertySchema,object?,Task<string?>> write,Action<string> feedback,Action<bool> actionBusy,Func<Task> refresh):this()
    {
        this.widget=widget;this.tile=tile;this.service=service;this.write=write;this.feedback=feedback;this.actionBusy=actionBusy;this.refresh=refresh;
        var children=widget.Children??[];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,widget.Label);
        if(widget.Kind=="colorPresets")
        {
            Colors.Visibility=Visibility.Visible;colorProperty=Property(widget);ColorLabel.Text=UiText.State(widget.Label);
            var defaults=new[]{0xFFFFFF,0xFFB347,0xFF4848,0x48A9FF,0x63D58B,0xB064FF};
            var names=new[]{"CardPresetWhite","CardPresetWarm","CardPresetRed","CardPresetBlue","CardPresetGreen","CardPresetPurple"};
            var options=widget.Options??defaults.Select((n,index)=>(Value:n,Label:UiText.Get(names[index]))).Where(option=>AdapterControlValues.IsValid(colorProperty,option.Value)).Select(option=>new AdapterOption(JsonSerializer.SerializeToElement(option.Value),option.Label)).ToList();
            rows.AddRange(options.Select(option=>new CompositeCardRow(widget,option.Label){Rgb=option.Value.GetInt32()}));
            SizeChanged+=(_,_)=>LayoutPalette();Loaded+=(_,_)=>LayoutPalette();
        }
        else if(widget.Kind=="thermostat")
        {
            Thermostat.Visibility=Visibility.Visible;
            readingWidget=children.Single(child=>child.Kind=="metric");
            var target=children.Single(child=>child.Kind=="slider");TargetHost.Content=Editor(target);
            modeWidget=children.SingleOrDefault(child=>child.Kind=="choice");
            if(modeWidget is {} mode)
            {
                modes=new(Property(mode).Values,mode.Presentation,compact:true);ModeHost.Content=modes;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modes,mode.Label);
                modes.ValueChosen+=async value=>await Send(mode,value);
            }
        }
        else if(widget.Kind=="media")
        {
            Media.Visibility=Visibility.Visible;
            readingWidget=children.SingleOrDefault(child=>child.Kind=="status");
            VolumeHost.Content=Editor(children.Single(child=>child.Kind=="slider"));
            rows.AddRange(children.Where(child=>child.Kind=="action").Select(child=>
            {
                var action=CardCompositePlan.Action(tile.Device,tile.State!.Schema,child);
                return new CompositeCardRow(child,child.Label){Action=action,Glyph=DeviceCardPresentation.Semantic(action.Action.ActionType) switch{"previous"=>"\uE892","pause"=>"\uE769","next"=>"\uE893",_=>"\uE768"}};
            }));
            Transport.ItemsSource=rows;
        }
        else
        {
            Switches.Visibility=Visibility.Visible;
            rows.AddRange(children.Select(child=>new CompositeCardRow(child,child.Label)));
            Switches.ItemsSource=rows;
        }
        Tapped+=(_,args)=>args.Handled=true;DoubleTapped+=(_,args)=>args.Handled=true;
        Unloaded+=(_,_)=>{active=false;ColorFlyout.Hide();};Loaded+=(_,_)=>{active=true;Update(this.tile,this.busy);};
        Update(tile,false);
    }
    private PropertySchema Property(AdapterWidget child)=>CardCompositePlan.Property(tile.Device,tile.State!.Schema,child);
    private bool Matches(AdapterWidget child)=>AdapterConditions.Matches(tile.Device.Adapter,tile.State,child.When)&&AdapterConditions.Matches(tile.Device.Adapter,tile.State,child.EnabledWhen);
    private bool Allowed(AdapterWidget child)=>active&&!tile.IsEditing&&tile.Device.Online&&Matches(widget)&&Matches(child);
    private bool Writable(AdapterWidget child)=>Allowed(child)&&!busy&&!sending&&DeviceExperience.CanWrite(tile.Device,tile.State,Property(child),null);
    private CardPropertyControl Editor(AdapterWidget child)
    {
        var editor=new CardPropertyControl(new(child,Property(child)),(_,value)=>Send(child,value),feedback);
        editors.Add((child,editor));return editor;
    }
    internal void Update(DeviceTile next,bool busy)
    {
        tile=next;this.busy=busy;
        foreach(var (child,editor) in editors)
        {
            editor.Visibility=AdapterConditions.Matches(tile.Device.Adapter,tile.State,child.When)?Visibility.Visible:Visibility.Collapsed;
            editor.Update(tile,busy||!Allowed(child));
        }
        if(readingWidget is {} reading)
        {
            var text=AdapterConditions.Matches(tile.Device.Adapter,tile.State,reading.When)?AdapterReadings.Format(tile.State,Property(reading)):"";
            if(widget.Kind=="thermostat")CurrentTemperature.Text=text;else PlaybackState.Text=text;
        }
        if(modeWidget is {} mode&&modes is not null)
        {
            modes.IsEnabled=Writable(mode);ModeHost.Visibility=AdapterConditions.Matches(tile.Device.Adapter,tile.State,mode.When)?Visibility.Visible:Visibility.Collapsed;
            modes.SetValue(DeviceCardPresentation.TryValue(tile.State,Property(mode),out var current)?current:null);
        }
        var playing=readingWidget is {} status?CardCompositePlan.Playing(tile.State,Property(status)):null;
        var hasPlayPause=rows.Any(row=>row.Widget.When is null&&row.Action is {} action&&DeviceCardPresentation.Semantic(action.Action.ActionType)=="play")&&
            rows.Any(row=>row.Widget.When is null&&row.Action is {} action&&DeviceCardPresentation.Semantic(action.Action.ActionType)=="pause");
        foreach(var row in rows)
        {
            row.Visibility=AdapterConditions.Matches(tile.Device.Adapter,tile.State,row.Widget.When)?Visibility.Visible:Visibility.Collapsed;
            if(row.Action is {} action)
            {
                row.Enabled=Allowed(row.Widget)&&!busy&&!sending&&!unknown&&CardCompositePlan.IsMediaAction(action.Action)&&
                    (tile.Device.Adapter is null?service is ISpeakerActionService:service is IDeviceActionService);
                if(playing is {} isPlaying&&hasPlayPause&&row.Widget.When is null)
                {
                    var name=DeviceCardPresentation.Semantic(action.Action.ActionType);
                    if(name=="play"&&isPlaying||name=="pause"&&!isPlaying)row.Visibility=Visibility.Collapsed;
                }
            }
            else
            {
                row.Enabled=Writable(row.Widget);
                var valid=DeviceCardPresentation.TryValue(tile.State,Property(row.Widget),out var value);
                row.Checked=valid?(row.Rgb is {} rgb?value.TryGetInt32(out var current)&&current==rgb:value.ValueKind==JsonValueKind.True):null;
                row.Status=UiText.Get(!valid?"UnknownState":row.Checked==true?"TurnedOn":"TurnedOff");
            }
        }
        if(colorProperty is not null)
        {
            ChooseColor.IsEnabled=Writable(widget);
            var valid=DeviceCardPresentation.TryValue(tile.State,colorProperty,out var value)&&value.TryGetInt32(out _);
            CurrentColor.Fill=valid?new SolidColorBrush(Color(value.GetInt32())):null;
            if(valid&&!colorOpen)Picker.Color=Color(value.GetInt32());
            ApplyColor.IsEnabled=Writable(widget)&&AdapterControlValues.IsValid(colorProperty,AdapterControlValues.Rgb(Picker.Color.R,Picker.Color.G,Picker.Color.B));
        }
    }
    private void LayoutPalette()
    {
        var count=Math.Min(rows.Count,Math.Clamp((int)((ActualWidth-38)/36),1,6));
        if(count==paletteCount)return;paletteCount=count;Palette.ItemsSource=rows.Take(count).ToList();
    }
    private async Task<string?> Send(AdapterWidget child,object? value)
    {
        if(!Writable(child)){Update(tile,busy);return UiText.Get("ConditionDisabled");}
        if(child.Kind=="colorPresets"&&value is int rgb&&!AdapterControlValues.IsValid(Property(child),rgb))return UiText.Get("ConditionDisabled");
        sending=true;
        try
        {
            var result=await write(Property(child),value);if(result is not null)feedback(result);return result;
        }
        catch(BridgeException error){feedback(error.Message);return error.Message;}
        finally{sending=false;Update(tile,busy);}
    }
    private async void OnPreset(object sender,RoutedEventArgs args)
    {if(((FrameworkElement)sender).DataContext is CompositeCardRow {Rgb:{} rgb} row)await Send(row.Widget,rgb);}
    private async void OnSwitch(object sender,RoutedEventArgs args)
    {
        if(((FrameworkElement)sender).DataContext is not CompositeCardRow row)return;
        if(!DeviceCardPresentation.TryValue(tile.State,Property(row.Widget),out var value)){Update(tile,busy);return;}
        await Send(row.Widget,value.ValueKind!=JsonValueKind.True);
    }
    private async void OnTransport(object sender,RoutedEventArgs args)
    {
        if(((FrameworkElement)sender).DataContext is not CompositeCardRow {Action:{} action} row||!row.Enabled||!Allowed(row.Widget)||!CardCompositePlan.IsMediaAction(action.Action))return;
        var gate=ActionControl.DeviceGate(tile.Device.Id);
        if(!await gate.WaitAsync(0))return;
        sending=true;
        try
        {
            actionBusy(true);Update(tile,true);
            if(!Allowed(row.Widget))return;
            var result=tile.Device.Adapter is {} adapter&&service is IDeviceActionService actions
                ?await actions.InvokeActionAsync(new(tile.Device.Id,adapter.Id,action.Id,[],Guid.NewGuid().ToString("N"),true))
                :await ((ISpeakerActionService)service).SendSpeakerActionAsync(new(tile.Device.Id,tile.Device.HomeId,DeviceCardPresentation.Semantic(action.Action.ActionType),Guid.NewGuid().ToString("N")));
            unknown=result.Status=="unknown";if(result.Status!="accepted")feedback(result.Message);
            await refresh();
        }
        catch(BridgeException error){unknown=error.Code=="actionUnknown";feedback(error.Message);}
        catch(Exception){unknown=true;feedback(UiText.Get("CardActionUnknown"));}
        finally{sending=false;gate.Release();actionBusy(false);Update(tile,false);}
    }
    private static Windows.UI.Color Color(int value)=>Windows.UI.Color.FromArgb(255,(byte)(value>>16),(byte)(value>>8),(byte)value);
    private void OnColorOpening(object sender,object args){colorOpen=true;if(colorProperty is not null&&DeviceCardPresentation.TryValue(tile.State,colorProperty,out var value)&&value.TryGetInt32(out var rgb))Picker.Color=Color(rgb);}
    private void OnColorClosed(object sender,object args){colorOpen=false;if(tile is not null)Update(tile,busy);}
    private void OnPickerChanged(ColorPicker sender,ColorChangedEventArgs args){if(colorProperty is not null)ApplyColor.IsEnabled=Writable(widget)&&AdapterControlValues.IsValid(colorProperty,AdapterControlValues.Rgb(sender.Color.R,sender.Color.G,sender.Color.B));}
    private void OnCancelColor(object sender,RoutedEventArgs args)=>ColorFlyout.Hide();
    private async void OnApplyColor(object sender,RoutedEventArgs args){await Send(widget,AdapterControlValues.Rgb(Picker.Color.R,Picker.Color.G,Picker.Color.B));ColorFlyout.Hide();}
}

public sealed class CompositeCardRow : ObservableObject
{
    internal AdapterWidget Widget {get;}
    internal AdapterAction? Action {get;init;}
    public string Label {get;}
    public string Glyph {get;init;}="";
    public int? Rgb {get;init;}
    public Brush Brush=>new SolidColorBrush(Windows.UI.Color.FromArgb(255,(byte)((Rgb??0)>>16),(byte)((Rgb??0)>>8),(byte)(Rgb??0)));
    private bool? selected;
    public bool? Checked {get=>selected;set{if(!SetProperty(ref selected,value))OnPropertyChanged(nameof(Checked));}}
    private bool enabled;
    public bool Enabled {get=>enabled;set=>SetProperty(ref enabled,value);}
    private Visibility visibility=Visibility.Visible;
    public Visibility Visibility {get=>visibility;set=>SetProperty(ref visibility,value);}
    private string status="";
    public string Status {get=>status;set=>SetProperty(ref status,value);}
    internal CompositeCardRow(AdapterWidget widget,string label){Widget=widget;Label=label;}
}
