using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Dialogs;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;
using MiHome.WinUI.ViewModels;
using Windows.Devices.Enumeration;

namespace MiHome.WinUI.Pages;

public sealed record AutomationRuleRow(AutomationRule Rule, string Detail)
{
    public string Name => Rule.Name;
}

public sealed partial class AutomationPage : UserControl
{
    private readonly ObservableCollection<AutomationStep> draft = [];
    private AutomationEngine Engine => App.Automation;
    private Func<Task<string?>>? pickProgram;
    private Guid ruleId;
    private string program = "";
    private ChoiceItem? savedUsb;
    private string? account;
    private IReadOnlyList<Home>? homes;
    private IReadOnlyList<Home>? ruleHomes;
    private IReadOnlyList<AutomationRule>? shownRules;
    private int sceneRevision, targetRevision, propertyRevision, usbRevision;
    private bool syncing, editing, executingScene;

    public AutomationPage() => InitializeComponent();

    internal void Configure(Func<Task<string?>> picker)
    {
        pickProgram = picker;
        Trigger.ItemsSource = new[] { "foreground", "foreground-exit", "process-start", "process-exit", "lock", "unlock", "hotkey",
            "idle", "active", "schedule", "wake", "ac-power", "battery-low", "display-off", "usb-attach", "audio-output", "network-online" }
            .Select(kind => Option(kind, AutomationRule.TriggerLabel(kind))).ToArray();
        Hotkey.ItemsSource = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(key => key.ToString()).ToList();
        StepType.ItemsSource = new[] { Option("write", "AutoWriteType"), Option("wait", "AutoWaitType"), Option("scene", "AutoSceneType") };
        Steps.ItemsSource = draft;
        Engine.Changed += UpdateState;
        Tabs.SelectedItem = Tabs.Items[0];
        Trigger.SelectedIndex = StepType.SelectedIndex = 0;
    }

    private static ChoiceItem Option(string value, string label) => new(value, UiText.Get(label));

    internal void Open()
    {
        UpdateState();
        if (ReferenceEquals(homes, App.Session.Homes)) return;
        homes = App.Session.Homes;
        var homeId = (SceneHome.SelectedItem as Home)?.Id ?? App.Session.Home?.Id;
        SceneHome.ItemsSource = homes;
        SceneHome.SelectedItem = homes.FirstOrDefault(home => home.Id == homeId) ?? homes.FirstOrDefault();
    }

    private void UpdateState()
    {
        if (account != Engine.Account)
        {
            account = Engine.Account;
            homes = null;
            sceneRevision++;
            Scenes.ItemsSource = null;
            SceneSurface.Visibility = Visibility.Collapsed;
            NoScenes.Visibility = Visibility.Collapsed;
            SceneLoading.IsActive = false;
            CloseEditor();
        }
        syncing = true;
        Pause.IsOn = Engine.Paused;
        Pause.IsEnabled = account is not null && !Engine.StorageInvalid;
        NewRule.IsEnabled = Pause.IsEnabled && App.Session.Homes.Count > 0;
        RuleList.IsEnabled = Pause.IsEnabled;
        syncing = false;
        if (!ReferenceEquals(shownRules, Engine.Rules) || !ReferenceEquals(ruleHomes, App.Session.Homes))
        {
            shownRules = Engine.Rules;
            ruleHomes = App.Session.Homes;
            RuleList.ItemsSource = shownRules.Select(RuleRow).ToList();
        }
        if (!ReferenceEquals(History.ItemsSource, Engine.History)) History.ItemsSource = Engine.History;
        HistoryEmpty.Visibility = Engine.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRules.Visibility = Engine.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var issue = App.AutomationTriggerIssue;
        TriggerIssue.Message = Engine.StorageInvalid ? UiText.Get("AutoStorage") :
            issue.Length == 0 ? "" : UiText.Format("AutoTriggerIssue", issue);
        TriggerIssue.IsOpen = TriggerIssue.Message.Length != 0;
    }

    private AutomationRuleRow RuleRow(AutomationRule rule)
    {
        var trigger = rule.Trigger switch
        {
            "hotkey" => $"Ctrl+Alt+{rule.Argument}",
            "usb-attach" => UiText.Format("AutoTriggerDetail", UiText.Get("AutoUsbAttach"), rule.TargetLabel ?? ""),
            "schedule" when AutomationRule.TrySchedule(rule.Argument, out var time, out var days) =>
                UiText.Format("AutoScheduleDetail", time.ToString("HH:mm", CultureInfo.InvariantCulture), FormatDays(days)),
            "idle" or "active" or "battery-low" => UiText.Format("AutoTriggerDetail", UiText.Get(AutomationRule.TriggerLabel(rule.Trigger)),
                UiText.Format(rule.Trigger == "battery-low" ? "AutoThresholdPercent" : "AutoThresholdMinutes", rule.Argument)),
            _ when AutomationRule.UsesProgram(rule.Trigger) =>
                UiText.Format("AutoTriggerDetail", UiText.Get(AutomationRule.TriggerLabel(rule.Trigger)), Path.GetFileName(rule.Argument)),
            _ => UiText.Get(AutomationRule.TriggerLabel(rule.Trigger))
        };
        var home = ruleHomes?.FirstOrDefault(item => item.Id == rule.HomeId)?.Name ?? UiText.Get("AutoHomeUnavailable");
        return new(rule, $"{trigger} · {home} · {UiText.Get(rule.Enabled ? "Enabled" : "Disabled")}");
    }

    private static string FormatDays(int days)
    {
        if (days == 127) return UiText.Get("AutoEveryDay");
        var names = CultureInfo.GetCultureInfo(UiText.Language).DateTimeFormat.AbbreviatedDayNames;
        return string.Join(" ", new[] { 1, 2, 3, 4, 5, 6, 0 }.Where(day => (days & (1 << day)) != 0).Select(day => names[day]));
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var rules = Tabs.SelectedItem == Tabs.Items[1];
        RulesPane.Visibility = rules ? Visibility.Visible : Visibility.Collapsed;
        ScenesPane.Visibility = rules ? Visibility.Collapsed : Visibility.Visible;
        Notice.IsOpen = false;
    }

    private async void OnSceneHomeChanged(object sender, SelectionChangedEventArgs args) => await LoadScenesAsync();
    private async void OnRefreshScenes(object sender, RoutedEventArgs args) => await LoadScenesAsync();
    private async Task LoadScenesAsync()
    {
        var revision = ++sceneRevision;
        Scenes.ItemsSource = null;
        SceneSurface.Visibility = Visibility.Collapsed;
        NoScenes.Visibility = Visibility.Collapsed;
        if (SceneHome.SelectedItem is not Home home || account is null) { SceneLoading.IsActive = false; return; }
        SceneLoading.IsActive = true;
        try
        {
            var scenes = await Engine.ScenesAsync(home.Id);
            if (revision != sceneRevision) return;
            Scenes.ItemsSource = scenes;
            SceneSurface.Visibility = Visibility.Visible;
            NoScenes.Visibility = scenes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { if (revision == sceneRevision) Message("AutoLoadScenesFailed"); }
        finally { if (revision == sceneRevision) SceneLoading.IsActive = false; }
    }

    private async void OnRunScene(object sender, ItemClickEventArgs args)
    {
        if (executingScene || args.ClickedItem is not CloudScene scene || XamlRoot is null) return;
        var owner = account;
        executingScene = true;
        try
        {
            if (!await ConfirmAsync(UiText.Get("AutoRunScene.Content"), UiText.Format("AutoConfirmScene", scene.Name))) return;
            if (owner is null || owner != Engine.Account) return;
            var accepted = await Engine.RunManualSceneAsync(scene);
            if (owner == Engine.Account) Message(accepted ? "AutoAccepted" : "AutoFailed", accepted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (BridgeException error) when (error.Code is "actionUnknown" or "network") { Message("AutoUnknown"); }
        catch { Message("AutoFailed"); }
        finally { executingScene = false; }
    }

    private void OnNewRule(object sender, RoutedEventArgs args) => StartEditor(null);
    private void OnEditRule(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is AutomationRuleRow row) StartEditor(row.Rule);
    }

    private async void OnDeleteRule(object sender, RoutedEventArgs args)
    {
        var rule = Engine.Rules.FirstOrDefault(item => item.Id == ruleId);
        if (!editing || rule is null || XamlRoot is null) return;
        var owner = account;
        try
        {
            if (!await ConfirmAsync(UiText.Get("AutoDeleteRule.Content"), rule.Name) || owner != Engine.Account || rule.Id != ruleId) return;
            Engine.Delete(rule.Id);
            CloseEditor();
            NewRule.Focus(FocusState.Programmatic);
        }
        catch { Message("AutoStorage"); }
    }

    private void OnPause(object sender, RoutedEventArgs args)
    {
        if (syncing) return;
        try { Engine.SetPaused(Pause.IsOn); }
        catch { UpdateState(); Message("AutoStorage"); }
    }

    private void StartEditor(AutomationRule? rule)
    {
        if (account is null || Engine.StorageInvalid) return;
        Notice.IsOpen = false;
        EditorTitle.Text = UiText.Get(rule is null ? "AutoNewRule.Content" : "AutoEditorEditTitle");
        DeleteRule.Visibility = rule is null ? Visibility.Collapsed : Visibility.Visible;
        ruleId = rule?.Id ?? Guid.NewGuid();
        RuleName.Text = rule?.Name ?? "";
        EnabledRule.IsOn = rule?.Enabled ?? false;
        program = rule is not null && AutomationRule.UsesProgram(rule.Trigger) ? rule.Argument : "";
        ProgramName.Text = Path.GetFileName(program);
        ToolTipService.SetToolTip(ProgramName, program);
        Trigger.SelectedItem = ((IEnumerable<ChoiceItem>)Trigger.ItemsSource).First(item => Equals(item.Value, rule?.Trigger ?? "foreground"));
        Hotkey.SelectedItem = rule?.Trigger == "hotkey" ? rule.Argument : "G";
        var schedule = TimeSpan.FromHours(18);
        var days = 127;
        if (rule?.Trigger == "schedule" && AutomationRule.TrySchedule(rule.Argument, out var time, out var mask))
        { schedule = time.ToTimeSpan(); days = mask; }
        ScheduleTime.Time = schedule;
        foreach (var day in ScheduleDays.Children.OfType<ToggleButton>()) day.IsChecked = (days & DayMask(day)) != 0;
        UpdateTriggerControls();
        if (rule?.Trigger is "idle" or "active" or "battery-low" && AutomationRule.TryThreshold(rule.Argument, 240, out var value))
            Threshold.Value = value;
        else Threshold.Value = rule?.Trigger == "battery-low" ? 20 : 15;
        usbRevision++;
        savedUsb = rule?.Trigger == "usb-attach" ? new ChoiceItem(rule.Argument, rule.TargetLabel!) : null;
        UsbDevice.ItemsSource = savedUsb is { } usb ? new[] { usb } : Array.Empty<ChoiceItem>();
        UsbDevice.SelectedItem = savedUsb;
        From.Time = TimeSpan.TryParse(rule?.From, CultureInfo.InvariantCulture, out var start) ? start : TimeSpan.FromHours(18);
        Until.Time = TimeSpan.TryParse(rule?.Until, CultureInfo.InvariantCulture, out var end) ? end : TimeSpan.FromHours(6);
        LimitTime.IsChecked = !string.IsNullOrEmpty(rule?.From);
        draft.Clear();
        foreach (var step in rule?.Steps ?? []) draft.Add(step);
        editing = false;
        Composer.IsExpanded = false;
        StepType.SelectedIndex = 0;
        Tabs.Visibility = ListScroll.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        RuleHome.ItemsSource = App.Session.Homes;
        var homeId = rule?.HomeId ?? App.Session.Home?.Id;
        var selectedHome = App.Session.Homes.FirstOrDefault(home => home.Id == homeId);
        RuleHome.SelectedItem = rule is null ? selectedHome ?? App.Session.Homes.FirstOrDefault() : selectedHome;
        RuleHome.IsEnabled = draft.Count == 0;
        editing = true;
        if (UsbDevice.Visibility == Visibility.Visible) _ = LoadUsbChoicesAsync();
        UpdateStepCommands();
        _ = LoadTargetsAsync();
        EditorScroll.ChangeView(null, 0, null, true);
        RuleName.Focus(FocusState.Programmatic);
    }

    private void CloseEditor()
    {
        editing = false;
        targetRevision++;
        propertyRevision++;
        usbRevision++;
        Editor.Visibility = Visibility.Collapsed;
        Tabs.Visibility = ListScroll.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        draft.Clear();
    }

    private void OnCancelEdit(object sender, RoutedEventArgs args)
    {
        CloseEditor();
        NewRule.Focus(FocusState.Programmatic);
    }
    private void OnTriggerChanged(object sender, SelectionChangedEventArgs args)
    {
        UpdateTriggerControls();
        if (editing && UsbDevice.Visibility == Visibility.Visible) _ = LoadUsbChoicesAsync();
    }

    private string? SelectedTrigger => (Trigger.SelectedItem as ChoiceItem)?.Value as string;

    private void UpdateTriggerControls()
    {
        var kind = SelectedTrigger;
        ProgramRow.Visibility = AutomationRule.UsesProgram(kind) ? Visibility.Visible : Visibility.Collapsed;
        Hotkey.Visibility = kind == "hotkey" ? Visibility.Visible : Visibility.Collapsed;
        ScheduleRow.Visibility = kind == "schedule" ? Visibility.Visible : Visibility.Collapsed;
        UsbDevice.Visibility = kind == "usb-attach" ? Visibility.Visible : Visibility.Collapsed;
        var battery = kind == "battery-low";
        ThresholdRow.Visibility = battery || kind is "idle" or "active" ? Visibility.Visible : Visibility.Collapsed;
        var maximum = battery ? 99 : 240;
        if (Threshold.Maximum != maximum)
        {
            Threshold.Maximum = maximum;
            Threshold.Value = battery ? 20 : 15;
        }
        ThresholdLabel.Text = UiText.Get(battery ? "AutoBatteryThreshold" : "AutoIdleThreshold");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Threshold, ThresholdLabel.Text);
        ShowThreshold();
    }

    private void OnThresholdChanged(object sender, RangeBaseValueChangedEventArgs args) => ShowThreshold();

    private void ShowThreshold()
    {
        if (ThresholdValue is null || Threshold is null) return;
        ThresholdValue.Text = UiText.Format(SelectedTrigger == "battery-low" ? "AutoThresholdPercent" : "AutoThresholdMinutes", (int)Threshold.Value);
    }

    private static int DayMask(ToggleButton day) => 1 << int.Parse((string)day.Tag, CultureInfo.InvariantCulture);

    private async void OnUsbChoicesOpened(object sender, object args) => await LoadUsbChoicesAsync();

    private async Task LoadUsbChoicesAsync()
    {
        var revision = ++usbRevision;
        try
        {
            var devices = await DeviceInformation.FindAllAsync(AutomationTriggers.UsbSelector, [], DeviceInformationKind.DeviceInterface);
            if (!editing || revision != usbRevision || SelectedTrigger != "usb-attach") return;
            var selected = UsbDevice.SelectedItem as ChoiceItem;
            var choices = devices.Where(device => device.Id.Length <= 1024 && device.Name.Length is > 0 and <= 256)
                .Select(device => new ChoiceItem(device.Id, device.Name)).OrderBy(choice => choice.Label, StringComparer.CurrentCulture).ToList();
            if ((selected ?? savedUsb) is { } previous && !choices.Any(choice => Equals(choice.Value, previous.Value))) choices.Add(previous);
            UsbDevice.ItemsSource = choices;
            UsbDevice.SelectedItem = choices.FirstOrDefault(choice => Equals(choice.Value, selected?.Value));
        }
        catch (Exception)
        {
            if (editing && revision == usbRevision && SelectedTrigger == "usb-attach") Message("AutoUsbUnavailable");
        }
    }

    private async void OnPickProgram(object sender, RoutedEventArgs args)
    {
        var id = ruleId;
        try
        {
            if (pickProgram is null || await pickProgram() is not { } path || !editing || id != ruleId) return;
            program = path;
            ProgramName.Text = Path.GetFileName(path);
            ToolTipService.SetToolTip(ProgramName, path);
        }
        catch { Message("AutoInvalid"); }
    }

    private void OnTimeChanged(object sender, RoutedEventArgs args) => TimeRow.Visibility = LimitTime.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    private void OnRuleHomeChanged(object sender, SelectionChangedEventArgs args) { if (editing) _ = LoadTargetsAsync(); }

    private async Task LoadTargetsAsync()
    {
        var revision = ++targetRevision;
        Device.ItemsSource = null;
        StepScene.ItemsSource = null;
        if (RuleHome.SelectedItem is not Home home) return;
        try
        {
            var devices = App.Session.Home?.Id == home.Id ? App.Session.Browser.AllDevices.ToList() :
                (await App.Session.Backend.CallAsync("ListDevices", new { homeId = home.Id })).Deserialize<List<DeviceIdentity>>(BackendClient.Json) ?? [];
            if (revision != targetRevision) return;
            Device.ItemsSource = DeviceListing.Visible(devices).Select(device => new ChoiceItem(device,
                string.IsNullOrEmpty(device.Room) ? device.Name : $"{device.Name} · {device.Room}")).ToList();
        }
        catch { if (revision == targetRevision) Message("AutoLoadDevicesFailed"); }
        if (StepType.SelectedItem is ChoiceItem { Value: "scene" } && revision == targetRevision) await LoadStepScenesAsync();
    }

    private async Task LoadStepScenesAsync()
    {
        if (StepScene.ItemsSource is not null || RuleHome.SelectedItem is not Home home) return;
        var revision = targetRevision;
        try
        {
            var scenes = await Engine.ScenesAsync(home.Id);
            if (revision == targetRevision) StepScene.ItemsSource = scenes;
        }
        catch { if (revision == targetRevision) Message("AutoLoadScenesFailed"); }
    }

    private async void OnDeviceChanged(object sender, SelectionChangedEventArgs args)
    {
        var revision = ++propertyRevision;
        Property.ItemsSource = null;
        if (Device.SelectedItem is not ChoiceItem { Value: DeviceIdentity device }) return;
        try
        {
            var schema = await App.Session.Backend.GetSchemaAsync(device.Id);
            if (revision != propertyRevision) return;
            Property.ItemsSource = schema.Properties.Where(property => AutomationRule.CanOffer(device, schema, property))
                .Select(property => new ChoiceItem(property, PropertyPresentation.Name(property))).ToList();
        }
        catch { if (revision == propertyRevision) Message("AutoLoadDevicesFailed"); }
    }

    private void OnPropertyChanged(object sender, SelectionChangedEventArgs args)
    {
        ChoiceValue.Visibility = NumberValue.Visibility = ColorValue.Visibility = SliderHost.Visibility = Visibility.Collapsed;
        if (Property.SelectedItem is not ChoiceItem { Value: PropertySchema property }) return;
        if (property.Type == "bool" || property.Values.Count > 0)
        {
            ChoiceValue.ItemsSource = property.Type == "bool"
                ? new[] { new ChoiceItem(JsonSerializer.SerializeToElement(true), UiText.Get("AutoOn")),
                    new ChoiceItem(JsonSerializer.SerializeToElement(false), UiText.Get("AutoOff")) }
                : property.Values.Select(option => new ChoiceItem(option.Value, PropertyPresentation.EnumLabel(property, option))).ToArray();
            ChoiceValue.SelectedIndex = 0;
            ChoiceValue.Visibility = Visibility.Visible;
        }
        else if (DeviceCardPresentation.Semantic(property.PropertyType) == "color") ColorValue.Visibility = Visibility.Visible;
        else if (property.Range.Length >= 2 && double.IsFinite(property.Range[0]) &&
                 double.IsFinite(property.Range[1]) && property.Range[1] > property.Range[0])
        {
            SliderValue.Minimum = property.Range[0];
            SliderValue.Maximum = property.Range[1];
            SliderValue.StepFrequency = property.Range.Length >= 3 && property.Range[2] > 0 ? property.Range[2] : 1;
            SliderValue.Value = property.Range[0];
            UpdateSliderLabel(property, SliderValue.Value);
            SliderHost.Visibility = Visibility.Visible;
        }
        else
        {
            NumberValue.Minimum = property.Range.Length >= 2 ? property.Range[0] : double.MinValue;
            NumberValue.Maximum = property.Range.Length >= 2 ? property.Range[1] : double.MaxValue;
            NumberValue.SmallChange = property.Range.Length >= 3 && property.Range[2] > 0 ? property.Range[2] : 1;
            NumberValue.Value = double.NaN;
            NumberValue.Visibility = Visibility.Visible;
        }
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (Property.SelectedItem is ChoiceItem { Value: PropertySchema property }) UpdateSliderLabel(property, args.NewValue);
    }

    private void UpdateSliderLabel(PropertySchema property, double value)
    {
        var unit = PropertyPresentation.Unit(property.Unit);
        SliderLabel.Text = value.ToString("0.######", CultureInfo.CurrentCulture) + (unit.Length > 0 ? $" {unit}" : "");
    }

    private async void OnStepTypeChanged(object sender, SelectionChangedEventArgs args)
    {
        var kind = (StepType.SelectedItem as ChoiceItem)?.Value as string;
        WriteFields.Visibility = kind == "write" ? Visibility.Visible : Visibility.Collapsed;
        StepScene.Visibility = kind == "scene" ? Visibility.Visible : Visibility.Collapsed;
        Seconds.Visibility = kind == "wait" ? Visibility.Visible : Visibility.Collapsed;
        if (editing && kind == "scene") await LoadStepScenesAsync();
    }

    private void OnAddStep(object sender, RoutedEventArgs args)
    {
        if (RuleHome.SelectedItem is not Home home || draft.Count >= 20) { Message("AutoInvalid"); return; }
        var kind = (StepType.SelectedItem as ChoiceItem)?.Value as string;
        AutomationStep? step = kind switch
        {
            "wait" when Seconds.Value is >= 1 and <= 300 && Math.Truncate(Seconds.Value) == Seconds.Value => new("wait", "", Seconds: (int)Seconds.Value),
            "scene" when StepScene.SelectedItem is CloudScene scene && scene.HomeId == home.Id =>
                new("scene", UiText.Format("AutoSceneLabel", scene.Name), scene.Id),
            "write" => BuildWrite(home.Id),
            _ => null
        };
        if (step is null) { Message("AutoInvalidStep"); return; }
        draft.Add(step);
        RuleHome.IsEnabled = false;
        Steps.SelectedItem = step;
        UpdateStepCommands();
        Notice.IsOpen = false;
    }

    private AutomationStep? BuildWrite(string homeId)
    {
        if (Device.SelectedItem is not ChoiceItem { Value: DeviceIdentity device } target || device.HomeId != homeId ||
            Property.SelectedItem is not ChoiceItem { Value: PropertySchema property } choice) return null;
        JsonElement value;
        string? colorLabel = null;
        if (ChoiceValue.Visibility == Visibility.Visible && ChoiceValue.SelectedItem is ChoiceItem { Value: JsonElement option }) value = option;
        else if (ColorValue.Visibility == Visibility.Visible)
        {
            var color = Color.Color;
            var rgb = color.R << 16 | color.G << 8 | color.B;
            value = JsonSerializer.SerializeToElement(rgb);
            colorLabel = $"#{rgb:X6}";
        }
        else if (SliderHost.Visibility == Visibility.Visible || NumberValue.Visibility == Visibility.Visible)
        {
            var number = SliderHost.Visibility == Visibility.Visible ? SliderValue.Value : NumberValue.Value;
            if (!double.IsFinite(number)) return null;
            if (property.Range.Length >= 2 && (number < property.Range[0] || number > property.Range[1])) return null;
            if (property.Range.Length >= 3 && property.Range[2] > 0)
            {
                var steps = (number - property.Range[0]) / property.Range[2];
                if (Math.Abs(steps - Math.Round(steps)) > 1e-6) return null;
            }
            if (property.Type is "float" or "double") value = JsonSerializer.SerializeToElement(number);
            else if (number == Math.Truncate(number) && number >= int.MinValue && number <= uint.MaxValue) value = JsonSerializer.SerializeToElement((long)number);
            else return null;
        }
        else return null;
        var label = $"{target.Label} · {choice.Label} · {colorLabel ?? PropertyPresentation.Value(property, value)}";
        return new("write", label, device.Id, device.SpecType, property.Siid, property.Piid, value);
    }

    private void OnMoveUp(object sender, RoutedEventArgs args) => MoveStep(-1);
    private void OnMoveDown(object sender, RoutedEventArgs args) => MoveStep(1);
    private void OnStepSelected(object sender, SelectionChangedEventArgs args) => UpdateStepCommands();
    private void OnStepDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => UpdateStepCommands();
    private void UpdateStepCommands()
    {
        var index = Steps.SelectedIndex;
        MoveUp.IsEnabled = index > 0;
        MoveDown.IsEnabled = index >= 0 && index < draft.Count - 1;
        RemoveStep.IsEnabled = index >= 0;
    }

    private void MoveStep(int direction)
    {
        var index = Steps.SelectedIndex;
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= draft.Count) return;
        draft.Move(index, destination);
        Steps.SelectedIndex = destination;
        UpdateStepCommands();
    }
    private void OnRemoveStep(object sender, RoutedEventArgs args)
    {
        var index = Steps.SelectedIndex;
        if (index >= 0 && index < draft.Count) draft.RemoveAt(index);
        RuleHome.IsEnabled = draft.Count == 0;
        UpdateStepCommands();
    }

    private AutomationRule? ReadDraft()
    {
        if (RuleHome.SelectedItem is not Home home || Trigger.SelectedItem is not ChoiceItem { Value: string trigger }) return null;
        var usb = UsbDevice.SelectedItem as ChoiceItem;
        string Format(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var argument = trigger switch
        {
            "hotkey" => Hotkey.SelectedItem as string ?? "",
            "idle" or "active" or "battery-low" => ((int)Threshold.Value).ToString(CultureInfo.InvariantCulture),
            "schedule" => Format(ScheduleTime.Time) + "|" + ScheduleDays.Children.OfType<ToggleButton>()
                .Where(day => day.IsChecked == true).Sum(DayMask).ToString(CultureInfo.InvariantCulture),
            "usb-attach" => usb?.Value as string ?? "",
            _ => AutomationRule.UsesProgram(trigger) ? program : ""
        };
        var rule = new AutomationRule(ruleId, RuleName.Text.Trim(), home.Id, EnabledRule.IsOn, trigger, argument,
            LimitTime.IsChecked == true ? Format(From.Time) : "", LimitTime.IsChecked == true ? Format(Until.Time) : "", draft.ToList(),
            trigger == "usb-attach" ? usb?.Label : null);
        return rule.IsValid() ? rule : null;
    }

    private async void OnSave(object sender, RoutedEventArgs args)
    {
        if (ReadDraft() is not { } rule) { Message("AutoInvalid"); return; }
        var owner = account;
        try
        {
            if (rule.Enabled && rule.Steps.Any(step => step.Kind == "scene") &&
                !await ConfirmAsync(UiText.Get("AutoEnable.Header"), UiText.Get("AutoConfirmSceneRule"))) return;
            if (!editing || rule.Id != ruleId || owner != Engine.Account) return;
            Engine.Save(rule);
            CloseEditor();
            NewRule.Focus(FocusState.Programmatic);
        }
        catch { Message("AutoStorage"); }
    }

    private async void OnPreview(object sender, RoutedEventArgs args)
    {
        if (ReadDraft() is not { } rule || XamlRoot is null) { Message("AutoInvalid"); return; }
        var heading = UiText.Get(rule.MatchesTime(TimeOnly.FromDateTime(DateTime.Now)) ? "AutoTimeMatches" : "AutoTimeNotMatched");
        var text = heading + "\n\n" + string.Join("\n", rule.Steps.Select((step, index) => $"{index + 1}. {step.Display}"));
        try
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Get("AutoPreview.Content"),
                Content = text, CloseButtonText = UiText.Get("AutoClose") }.ShowAsync();
        }
        catch { Message("AutoInvalid"); }
    }

    private async Task<bool> ConfirmAsync(string title, string text) => await new ContentDialog
    {
        XamlRoot = XamlRoot, Title = title, Content = text, PrimaryButtonText = UiText.Get("AutoConfirm"),
        CloseButtonText = UiText.Get("AutoCancelEdit.Content"), DefaultButton = ContentDialogButton.Close
    }.ShowAsync() == ContentDialogResult.Primary;

    private void Message(string key, InfoBarSeverity severity = InfoBarSeverity.Warning)
    {
        Notice.Message = UiText.Get(key);
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }
}
