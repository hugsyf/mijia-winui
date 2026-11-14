using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed partial class ExpandedCardView : UserControl
{
    private DeviceTile tile = null!;
    private readonly DeviceControlSession session = null!;
    private readonly List<(ExpandedCardItem Item, FrameworkElement Control)> controls = [];
    private readonly PropertySchema? powerProperty;
    private readonly AdapterWidget? powerWidget;
    private bool external, actionBusy;
    public event Action<DeviceCardState>? StateRead;
    public event Action<string>? Feedback;
    public ExpandedCardView() => InitializeComponent();
    internal ExpandedCardView(DeviceTile tile,List<ExpandedCardItem> items,IDeviceService service) : this()
    {
        this.tile=tile; session=new(tile.Device,tile.State!.Schema,service);
        DeviceIcon.Tile = tile; DeviceName.Text = tile.Name; ToolTipService.SetToolTip(DeviceName, tile.Name);
        if(tile.Device.Adapter is {} adapter)
        {
            var toggle=adapter.Card.FirstOrDefault(w=>w.Kind=="toggle");
            powerWidget=toggle;
            if(toggle is not null)powerProperty=adapter.Bindings.Single(b=>b.Id==toggle.Binding).Property;
        }
        else
        {
            var properties=DeviceExperience.PowerProperties(tile.Device,tile.State.Schema);
            if(properties.Count==1)powerProperty=properties[0];
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(power,tile.Name+"电源");
        power.Click+=async(_,e)=>{if(powerProperty is not null)await Write(powerProperty,power.IsChecked==true);};
        foreach(var item in items)
        {
            FrameworkElement control;
            if(CardCompositePlan.IsComposite(item.Widget.Kind))
                control=new CardCompositeControl(item.Widget,tile,service,
                    (property,value)=>AdapterConditions.Matches(this.tile.Device.Adapter,this.tile.State,item.Widget.EnabledWhen)?Write(property,value):Task.FromResult<string?>(Infrastructure.UiText.Get("ConditionDisabled")),
                    message=>Feedback?.Invoke(message),busy=>{actionBusy=busy;UpdateControls();},session.RefreshAsync);
            else if(item.Action is {} action)
            {
                var button=new ActionControl(tile.Device,action,item.Widget,service,compact:true);
                button.SetGuard(()=>AdapterConditions.Matches(tile.Device.Adapter,tile.State,item.Widget.EnabledWhen));
                button.BusyChanged+=busy=>{actionBusy=busy;UpdateControls();};
                button.Executed+=async()=>{try{await session.RefreshAsync();}catch(BridgeException e){Feedback?.Invoke(e.Message);}};
                control=button;
                control.VerticalAlignment=VerticalAlignment.Bottom;
            }
            else control=new CardPropertyControl(item,(property,value)=>AdapterConditions.Matches(this.tile.Device.Adapter,this.tile.State,item.Widget.EnabledWhen)?Write(property,value):Task.FromResult<string?>(Infrastructure.UiText.Get("ConditionDisabled")),message=>Feedback?.Invoke(message));
            controls.Add((item,control));content.ColumnDefinitions.Add(new());Grid.SetColumn(control,controls.Count-1);content.Children.Add(control);
        }
        session.StateRead+=snapshot=>{if(!external)StateRead?.Invoke(snapshot);};
        session.Changed+=UpdateControls;
        SizeChanged+=(_,_)=>UpdateControls();
        Update(tile);
    }
    public void Update(DeviceTile next)
    {
        tile=next;
        if(!session.Busy&&next.State is {} snapshot)
        {external=true;try{session.Accept(snapshot);}finally{external=false;}}
        UpdateControls();
    }
    private async Task<string?> Write(PropertySchema property,object? value)
    {
        if(tile.IsEditing||actionBusy||session.Busy)return "此设备正在处理另一项操作。";
        if(powerProperty==property&&!AdapterConditions.Matches(tile.Device.Adapter,tile.State,powerWidget?.EnabledWhen))return Infrastructure.UiText.Get("ConditionDisabled");
        try{await session.WriteAsync(property,value);}
        catch(BridgeException e){Feedback?.Invoke(e.Message);return e.Message;}
        if(session.Feedback is {} message)Feedback?.Invoke(message);
        UpdateControls();return session.Feedback;
    }
    private void UpdateControls()
    {
        if (session is null) return;
        var busy=session.Busy||actionBusy;
        subtitle.Text=tile.Room+(tile.Device.Online?"":" · 离线");
        if(powerProperty is not null)
        {
            var valid=DeviceCardPresentation.TryValue(tile.State,powerProperty,out var current);
            power.IsChecked=valid&&current.GetBoolean();
            power.IsEnabled=!tile.IsEditing&&!busy&&DeviceExperience.CanWrite(tile.Device,tile.State,powerProperty,power.IsChecked!=true)&&AdapterConditions.Matches(tile.Device.Adapter,tile.State,powerWidget?.EnabledWhen);
        }
        power.Visibility=powerProperty is null?Visibility.Collapsed:Visibility.Visible;
        if(powerWidget?.When is {} condition && tile.Device.Adapter is {} declared)
        {
            var p=declared.Bindings.Single(b=>b.Id==condition.Binding).Property;
            if(!DeviceCardPresentation.TryValue(tile.State,p,out var v)||!JsonElement.DeepEquals(v,condition.Value))power.Visibility=Visibility.Collapsed;
        }
        if(powerProperty is not null&&DeviceExperience.IsWireless(tile.Device,tile.State,powerProperty.Siid))power.Visibility=Visibility.Collapsed;
        ToolTipService.SetToolTip(this,tile.StateHint);
        var column=0;
        foreach(var (item,control) in controls)
        {
            var visible=column==0||ActualWidth>=380;
            if(item.Widget.When is {} when && tile.Device.Adapter is {} adapter)
            {
                var p=adapter.Bindings.Single(b=>b.Id==when.Binding).Property;
                visible &= DeviceCardPresentation.TryValue(tile.State,p,out var value)&&JsonElement.DeepEquals(value,when.Value);
            }
            control.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            content.ColumnDefinitions[controls.IndexOf((item,control))].Width=visible?new GridLength(1,GridUnitType.Star):new GridLength(0);
            if(visible)column++;
            var enabled=AdapterConditions.Matches(tile.Device.Adapter,tile.State,item.Widget.EnabledWhen);
            ((Control)control).IsEnabled=enabled;
            if(control is CardPropertyControl property)property.Update(tile,busy||!enabled);
            if(control is ActionControl action)action.SetAvailable(tile.Device.Online&&!tile.IsEditing&&!busy&&enabled);
            if(control is CardCompositeControl composite)composite.Update(tile,busy||!enabled);
        }
        content.ColumnSpacing=column>1?16:0;
    }
}
