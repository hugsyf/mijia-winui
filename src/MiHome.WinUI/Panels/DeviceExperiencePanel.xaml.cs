using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Panels;

public sealed partial class DeviceExperiencePanel : UserControl, IRefreshablePanel
{
    private readonly PanelContext context = null!;
    private readonly DeviceControlSession session = null!;
    private readonly HashSet<(int, int)> displayed = [];
    private readonly List<Action<DeviceCardState?>> update = [];
    private bool hasAdvanced;
    private GenericPanel? advancedPanel;
    private bool updating;
    public event Action<DeviceCardState>? CardStateRead;
    public DeviceExperiencePanel() => InitializeComponent();
    public DeviceExperiencePanel(PanelContext context) : this()
    {
        this.context = context;
        session = new(context.Device, context.Schema, context.Service);
        feedback.Closed += (_, _) => feedback.Visibility = Visibility.Collapsed;
        switch (DeviceExperience.Kind(context.Device, context.Schema))
        {
            case ExperienceKind.Router:
                AddMetric(primary, "已连接设备", "router", "connected-device-number", "台");
                AddRouterSpeed(primary, "下载速度", "download-speed");
                AddRouterSpeed(primary, "上传速度", "upload-speed");
                break;
            case ExperienceKind.Switch:
                AddPower(primary);
                break;
            case ExperienceKind.Light:
                var lightingGroup=new ControlGroupView();var light=lightingGroup.ItemsHost;
                AddPower(light, wrap:false);
                AddLightAdjustment(light, "brightness", wrap:false);
                AddLightAdjustment(light, "color-temperature", wrap:false);
                AddLightAdjustment(light, "color", wrap:false);
                primary.Children.Add(lightingGroup);
                break;
            case ExperienceKind.Outlet:
                AddPower(primary, includePower: true);
                break;
            case ExperienceKind.TemperatureHumidity:
                AddEnvironment(primary);
                AddMetric(secondary, "电量", "battery", "battery-level", "%", compact: true);
                break;
            case ExperienceKind.Occupancy:
                AddPresence(primary);
                AddMetric(secondary, "照度", "occupancy-sensor", "illumination", "lx", compact: true);
                AddMetric(secondary, "电量", "battery", "battery-level", "%", compact: true);
                AddMetric(secondary, "电量", "single-use-battery", "battery-level", "%", compact: true);
                break;
            case ExperienceKind.Humidifier:
                var humidityGroup=new ControlGroupView();var controls=humidityGroup.ItemsHost;controls.Spacing=20;
                AddPower(primary, includeHumidity: true);
                AddModes(controls, wrap: false);
                AddHumidityTarget(controls, wrap: false);
                primary.Children.Add(humidityGroup);
                AddMetric(secondary, "滤芯剩余", "filter", "filter-life-level", "%", compact: true);
                break;
        }
        foreach (var p in context.Schema.Properties.Where(p => DeviceCardPresentation.Semantic(p.PropertyType) == "fault"))
            displayed.Add((p.Siid, p.Piid));
        var remaining = context.Schema.Properties.Where(p => !displayed.Contains((p.Siid, p.Piid))).ToList();
        if (remaining.Count > 0)
        {
            hasAdvanced = true;
            advanced.Expanding += (_, _) =>
            {
                if (advancedPanel is not null) return;
                advancedPanel = new(new(context.Device, new(remaining), context.Service));
                advancedPanel.CardStateRead += state => session.Accept(state);
                if (session.State is { } state) advancedPanel.ApplySnapshot(state.Values);
                advanced.Content = advancedPanel;
            };
        }
        secondary.Visibility = secondary.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        session.StateRead += state => CardStateRead?.Invoke(state);
        session.Changed += Update;
        Update();
    }
    private IEnumerable<PropertySchema> DisplayProperties => DeviceModels.Find(context.Device) is not null
        ? context.Schema.Properties : DeviceCapabilities.Match(context.Device, context.Schema)?.Properties ?? [];
    private PropertySchema? Find(string service, string property) => DisplayProperties.FirstOrDefault(p => p.Readable &&
        DeviceCardPresentation.Semantic(p.ServiceType) == service && DeviceCardPresentation.Semantic(p.PropertyType) == property);
    private void AddMetric(StackPanel target, string label, string service, string property, string unit, bool compact = false)
    {
        var schema = Find(service, property);
        if (schema is null) return;
        displayed.Add((schema.Siid, schema.Piid));
        var metric=new MetricView(label,compact);var value=metric.ValueLabel;target.Children.Add(metric);
        update.Add(state => value.Text = DeviceCardPresentation.Number(state, service, property) is { } n
            ? n.ToString("0.#", CultureInfo.CurrentCulture) + " " + unit : "—");
    }
    private void AddRouterSpeed(StackPanel target, string label, string name)
    {
        var property = Find("router", name);
        if (property is null) return;
        displayed.Add((property.Siid, property.Piid));
        var metric=new MetricView(label);var value=metric.ValueLabel;value.Style=(Style)Application.Current.Resources["SubtitleTextBlockStyle"];target.Children.Add(metric);
        update.Add(state => value.Text = DeviceCardPresentation.RouterSpeed(state, name) ?? "—");
    }
    private void AddEnvironment(StackPanel target)
    {
        var metrics=new List<FrameworkElement>();
        foreach (var item in new[] { ("温度", "temperature", "°C"), ("湿度", "relative-humidity", "%") })
        {
            var property=Find("temperature-humidity-sensor",item.Item2);
            if(property is null)continue;
            displayed.Add((property.Siid,property.Piid));
            var metric=new MetricView(item.Item1,surface:false);metrics.Add(metric);var value=metric.ValueLabel;
            update.Add(state=>value.Text=DeviceCardPresentation.Number(state,"temperature-humidity-sensor",item.Item2) is {} n
                ?n.ToString("0.#",CultureInfo.CurrentCulture)+" "+item.Item3:"—");
        }
        var group=new ControlGroupView();group.ItemsHost.Children.Add(new ReadingsGrid(metrics));target.Children.Add(group);
    }

    private void AddPresence(StackPanel target)
    {
        var metric=new MetricView("当前状态");var value=metric.ValueLabel;value.Text=MiHome.WinUI.Infrastructure.UiText.Get("S93e474c68ed5");var duration=metric.CaptionLabel;target.Children.Add(metric);
        foreach (var p in context.Schema.Properties.Where(p => DeviceCardPresentation.Semantic(p.ServiceType) == "occupancy-sensor" &&
            DeviceCardPresentation.Semantic(p.PropertyType) is "occupancy-status" or "no-one-duration" or "has-someone-duration")) displayed.Add((p.Siid, p.Piid));
        update.Add(state =>
        {
            var occupied = DeviceCardPresentation.Number(state, "occupancy-sensor", "occupancy-status");
            value.Text = DeviceCardPresentation.Presence(state) is { Length: > 0 } presence ? presence : "状态未知";
            var minutes = occupied is null ? null : DeviceCardPresentation.Number(state, "occupancy-sensor", occupied == 0 ? "no-one-duration" : "has-someone-duration");
            duration.Text = minutes is { } n ? Infrastructure.UiText.Format("PresenceDuration",n.ToString("0")) : "";
            duration.Visibility = duration.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }
    private void AddPower(StackPanel target, bool includePower = false, bool includeHumidity = false, bool wrap = true)
    {
        var properties = DeviceExperience.PowerReadings(context.Device, context.Schema);
        foreach (var property in properties)
        {
            displayed.Add((property.Siid, property.Piid));
            var label = properties.Count > 1 ? DeviceModels.ChannelLabel(context.Device, property.Siid) : "电源";
            var view=new PowerStatusView();view.Configure(label,includeHumidity,wrap);
            var stateText=view.StateLabel;var toggle=view.PowerToggle;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,context.Device.Name+" "+label);
            if(includeHumidity)
            {
                var reading=Find("environment","relative-humidity");if(reading is not null)displayed.Add((reading.Siid,reading.Piid));
                update.Add(state=>view.HumidityLabel.Text=DeviceCardPresentation.Number(state,"environment","relative-humidity") is {} n?$"{n:0.#}%":"—");
            }
            if(includePower)
            {
                view.AdditionalReadings.Children.Add(Divider());
                AddMetric(view.AdditionalReadings,"当前功率","power-consumption","electric-power","W",compact:true);
            }
            target.Children.Add(view);
            toggle.Toggled += async (_, _) => { if (!updating) await ApplyAsync(property, toggle.IsOn); };
            update.Add(state =>
            {
                var wireless = DeviceExperience.IsWireless(context.Device, state, property.Siid);
                var valid = state is not null && DeviceCardPresentation.TryValue(state, property, out _);
                stateText.Text=Infrastructure.UiText.State(wireless?"无线模式":valid&&DeviceCardPresentation.TryValue(state,property,out var power)?power.GetBoolean()?"已开启":"已关闭":"状态未知，请刷新");
                stateText.Visibility = stateText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                toggle.Visibility = wireless || !property.Writable ? Visibility.Collapsed : Visibility.Visible;
                if (valid && DeviceCardPresentation.TryValue(state, property, out var value)) toggle.IsOn = value.GetBoolean();
                toggle.IsEnabled = !session.Busy && valid && DeviceExperience.CanWrite(context.Device, state, property, !toggle.IsOn);
            });
        }
    }
    private void AddModes(StackPanel target, bool wrap = true)
    {
        var property = Find("humidifier", "mode");
        if (property is not { Writable: true } || property.Values.Count == 0) return;
        displayed.Add((property.Siid, property.Piid));
        var labels = property.Values.Select(v => context.Device.Model == "xiaomi.humidifier.600" && v.Value.TryGetInt32(out var n)
            ? v with { DisplayDescription = n switch { 0 => "恒湿", 1 => "睡眠", 2 => "强力", 3 => "风干", _ => v.Description } } : v).ToList();
        if (!wrap && target.Children.Count > 0) target.Children.Add(Divider());
        AddValueEditor(target, property with { Values = labels }, new("choice", "运行模式", null, null, null), wrap);
    }

    private static FrameworkElement Divider() => new SeparatorLine();
    private void AddHumidityTarget(StackPanel target, bool wrap = true)
    {
        var property = Find("humidifier", "target-humidity");
        if (property is not { Writable: true } || property.Range.Length < 2) return;
        displayed.Add((property.Siid, property.Piid));
        if (!wrap && target.Children.Count > 0) target.Children.Add(Divider());
        AddValueEditor(target, property, new("slider", "目标湿度", null, null, null), wrap);
    }

    private void AddLightAdjustment(StackPanel target, string name, bool wrap = true)
    {
        var property = Find("light", name);
        if (property is null) return;
        var kind = name switch { "brightness" => "brightness", "color-temperature" => "colorTemperature", _ => "color" };
        var label = name switch { "brightness" => "亮度", "color-temperature" => "色温", _ => "颜色" };
        if (!property.Writable || property.Range.Length < 2 || name == "color" && property.Unit != "rgb") return;
        if (name == "brightness" && property.Unit != "percentage" || name == "color-temperature" && property.Unit != "kelvin") return;
        displayed.Add((property.Siid, property.Piid));
        AddValueEditor(target, property, new(kind, label, null, null, null), wrap);
    }
    private void AddValueEditor(StackPanel target, PropertySchema property, AdapterWidget widget, bool wrap)
    {
        AdapterValueEditor? row = null;
        row = new(widget, property, async value =>
        {
            await ApplyAsync(property, value);
            if (session.Feedback is {} message) row!.ShowError(message);
        }, surface: wrap);
        target.Children.Add(row);
        update.Add(state => row.SetValue(DeviceCardPresentation.TryValue(state, property, out var value) ? value : null,
            !session.Busy && DeviceExperience.CanWrite(context.Device, state, property, null)));
    }

    private async Task ApplyAsync(PropertySchema property, object? value)
    {
        try { await session.WriteAsync(property, value); }
        catch (BridgeException e) { feedback.Message = e.Message; feedback.IsOpen = true; }
    }
    private void Update()
    {
        updating = true;
        try
        {
            foreach (var action in update) action(session.State);
            if (session.State is { } state) advancedPanel?.ApplySnapshot(state.Values);
            advanced.IsEnabled = !session.Busy;
            feedback.Message = session.Feedback ?? session.State?.Error ?? "";
            feedback.IsOpen = feedback.Message.Length > 0;
            feedback.Visibility = feedback.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            var summary = DeviceCardPresentation.Summary(context.Device, session.State);
            fault.Message = Infrastructure.UiText.State(summary);
            fault.IsOpen = summary is "缺水" or "设备异常，请查看详情";
            fault.Visibility = fault.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { updating = false; }
    }
    public void SetExpanded(bool expanded) => advanced.Visibility = expanded && hasAdvanced ? Visibility.Visible : Visibility.Collapsed;
    public Task RefreshAsync() => session.RefreshAsync();
}
