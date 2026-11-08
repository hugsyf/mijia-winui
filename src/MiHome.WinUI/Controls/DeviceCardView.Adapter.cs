using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

public sealed partial class DeviceCardView
{
    private void RenderAdapter(DeviceTile tile,string size)
    {
        var adapter=tile.Device.Adapter!;
        var toggle=adapter.Card.FirstOrDefault(w=>w.Kind=="toggle" && (w.When is not {} condition ||
            DeviceCardPresentation.TryValue(tile.State,adapter.Bindings.Single(b=>b.Id==condition.Binding).Property,out var value) && System.Text.Json.JsonElement.DeepEquals(value,condition.Value)));
        ConfigureShell(tile,size,size=="compact"?tile.Room+" · "+Infrastructure.UiText.State(tile.Summary):tile.Room,
            toggle is null ? null : adapter.Bindings.Single(b=>b.Id==toggle.Binding).Property);
        cardPowerCondition=toggle?.EnabledWhen;
        CardPower.IsEnabled &= AdapterConditions.Matches(adapter,tile.State,cardPowerCondition);
        if(size!="compact")
        {
            var metrics=new CardMetricsView();var readings=new List<CardMetric>();
            foreach(var widget in adapter.Card.Where(w=>w.Kind!="toggle"))
            {
                var binding=adapter.Bindings.Single(b=>b.Id==widget.Binding);
                if(widget.When is {} condition)
                {
                    var cp=adapter.Bindings.Single(b=>b.Id==condition.Binding).Property;
                    if(!DeviceCardPresentation.TryValue(tile.State,cp,out var cv)||!System.Text.Json.JsonElement.DeepEquals(cv,condition.Value))continue;
                }
                var localized=AdapterLocalizationResolver.Widget(adapter,widget);
                readings.Add(new(localized.Label,AdapterReadings.Format(tile.State,AdapterControlValues.WithLabels(binding.Property,localized.Options)),FontSize:20));
            }
            metrics.SetReadings(readings);CardDetail.Content=metrics;
        }

    }
}
