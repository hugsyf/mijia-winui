using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.ViewModels;

namespace MiHome.WinUI.Panels;

public sealed partial class SpeakerPanel : UserControl, IRefreshablePanel
{
    private const string Directive = "execute-text-directive";
    private const string Speak = "play-text";
    private static readonly string[] Commands = ["previous", "play", "pause", "next", Directive, Speak];
    private PanelContext context = null!;
    private DeviceControlSession session = null!;
    private DispatcherQueueTimer volumeTimer = null!;
    private Func<bool> canInteract = () => false;
    private readonly HashSet<string> commands = [];
    private readonly bool embedded;
    private PropertySchema? volume, mute, playing;
    private IReadOnlyList<DeviceIdentity> speakerChoices = [];
    private bool painting, dragging, volumeDirty, sending, updatingSpeakerIdentity;
    private string playPauseCommand = "play";

    public SpeakerPanel() => InitializeComponent();

    internal SpeakerPanel(bool embedded) : this()
    {
        this.embedded = embedded;
        PanelSurface.Background = null;
        PanelSurface.BorderThickness = new Thickness(0);
        PanelSurface.Padding = new Thickness(0);
    }

    internal SpeakerPanel(PanelContext context, DeviceCardState? initial, Func<bool> canInteract) : this()
        => Configure(context, initial, canInteract);

    internal bool IsReady => session is not null;

    internal void Configure(PanelContext context, DeviceCardState? initial, Func<bool> canInteract)
    {
        this.context = context;
        this.canInteract = canInteract;
        session = new(context.Device, context.Schema, context.Service);
        volume = VolumeProperty(context.Schema);
        mute = FindProperty(context.Schema, "speaker", "mute") is { Writable: true, Type: "bool" } property ? property : null;
        playing = FindProperty(context.Schema, "play-control", "playing-state");
        if (context.Service is ISpeakerActionService)
            commands.UnionWith(Commands.Where(command => context.Schema.Actions.Count(action => Matches(action, command)) == 1));

        painting = true;
        if (volume is not null)
        {
            Volume.Minimum = volume.Range[0];
            Volume.Maximum = volume.Range[1];
            Volume.StepFrequency = volume.Range.Length > 2 ? volume.Range[2] : 1;
        }
        painting = false;
        volumeTimer = DispatcherQueue.CreateTimer();
        volumeTimer.Interval = TimeSpan.FromMilliseconds(350);
        volumeTimer.IsRepeating = false;
        volumeTimer.Tick += async (_, _) => await SubmitVolumeAsync();
        Volume.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { dragging = true; volumeTimer.Stop(); }), true);
        Volume.AddHandler(PointerReleasedEvent, new PointerEventHandler(async (_, _) => { dragging = false; await SubmitVolumeAsync(); }), true);
        Volume.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => CancelVolume()), true);
        Unloaded += (_, _) => ClearDraft();
        session.Changed += UpdateControls;
        if (initial is not null) session.Accept(initial);
        UpdateControls();
    }

    internal event Action<DeviceCardState>? StateRead { add => session.StateRead += value; remove => session.StateRead -= value; }
    internal event Action? AuthenticationLost;
    internal event Action<DeviceIdentity>? SpeakerChanged;

    internal void SetTrayIdentity(DeviceTile tile, IReadOnlyList<DeviceIdentity> choices, bool canChoose)
    {
        updatingSpeakerIdentity = true;
        try
        {
            if (SpeakerIcon.Tile?.Device != tile.Device) SpeakerIcon.Tile = tile;
            SpeakerIcon.Visibility = Visibility.Visible;
            SpeakerName.Text = tile.Name;
            SpeakerName.Visibility = Visible(choices.Count == 1);
            SpeakerPicker.Visibility = Visible(choices.Count > 1);
            SpeakerPicker.IsEnabled = canChoose;
            if (!speakerChoices.SequenceEqual(choices)) SpeakerPicker.ItemsSource = speakerChoices = choices;
            if (SpeakerPicker.SelectedItem is not DeviceIdentity selected || selected.Id != tile.Device.Id)
                SpeakerPicker.SelectedItem = speakerChoices.FirstOrDefault(device => device.Id == tile.Device.Id);
        }
        finally { updatingSpeakerIdentity = false; }
    }

    internal void SetPendingStatus(string text)
    {
        if (!IsReady) PlaybackState.Text = text;
    }

    private void OnSpeakerSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!updatingSpeakerIdentity && SpeakerPicker.SelectedItem is DeviceIdentity device) SpeakerChanged?.Invoke(device);
    }

    internal static bool IsCandidate(DeviceIdentity device) => device.Adapter is null && device.AdapterIssue is null &&
        (Semantic(device.SpecType) == "speaker" || device.Model.Contains(".wifispeaker.", StringComparison.Ordinal));

    internal static bool Supports(PanelContext context) => IsCandidate(context.Device) &&
        (VolumeProperty(context.Schema) is not null || Commands.Any(command => context.Schema.Actions.Count(action => Matches(action, command)) == 1));

    private static string Semantic(string type) => DeviceCardPresentation.Semantic(type);
    private static PropertySchema? FindProperty(DeviceSchema schema, string service, string property) => schema.Properties.FirstOrDefault(
        item => item.Readable && Semantic(item.ServiceType) == service && Semantic(item.PropertyType) == property);
    private static PropertySchema? VolumeProperty(DeviceSchema schema) => FindProperty(schema, "speaker", "volume") is
        { Writable: true, Range.Length: >= 2 } property && double.IsFinite(property.Range[0]) && double.IsFinite(property.Range[1]) &&
        property.Range[0] < property.Range[1] && (property.Range.Length < 3 || property.Range[2] > 0 && double.IsFinite(property.Range[2]))
        ? property : null;

    private static bool Matches(ActionSchema action, string command)
    {
        if (Semantic(action.ActionType) != command) return false;
        if (command is not (Directive or Speak)) return Semantic(action.ServiceType) == "play-control" && action.Inputs.Count == 0;
        if (Semantic(action.ServiceType) != "intelligent-speaker" || action.Inputs.Count != (command == Speak ? 1 : 2) ||
            action.Inputs[0].Type != "string" || Semantic(action.Inputs[0].PropertyType) != "text-content") return false;
        return command == Speak || action.Inputs[1].Type == "bool" && Semantic(action.Inputs[1].PropertyType) == "silent-execution";
    }

    public Task RefreshAsync() => !context.Device.Online || sending ? Task.CompletedTask : session.RefreshAsync();

    internal void ClearDraft()
    {
        if (session is null) return;
        TextFlyout.Hide();
        TextInput.Text = "";
        Quiet.IsChecked = false;
        CancelVolume();
    }

    private bool CanInteract => context.Device.Online && canInteract() && !sending && !session.Busy;
    private string? PlaybackKey()
    {
        if (playing is null || !DeviceCardPresentation.TryValue(session.State, playing, out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var state)) return null;
        return state switch { 0 => "Stopped", 1 => "Playing", 2 => "Paused", _ => null };
    }

    internal void UpdateControls()
    {
        if (session is null) return;
        var state = PlaybackKey();
        PlaybackState.Text = UiText.Get(!context.Device.Online ? "SpeakerOffline" : state ?? "SpeakerPlaybackUnknown");
        playPauseCommand = state == "Playing" && commands.Contains("pause") || !commands.Contains("play") ? "pause" : "play";
        PlayPauseIcon.Glyph = playPauseCommand == "pause" ? "\uE769" : "\uE768";
        SetLabel(PlayPause, playPauseCommand == "pause" ? "SpeakerPause" : "SpeakerPlay");
        SetButton(PlayPause, commands.Contains(playPauseCommand), playPauseCommand == "pause" ? state != "Stopped" : state != "Playing");
        SetButton(Previous, commands.Contains("previous"));
        SetButton(Next, commands.Contains("next"));
        Transport.Visibility = Visible(Previous.Visibility == Visibility.Visible || PlayPause.Visibility == Visibility.Visible || Next.Visibility == Visibility.Visible);
        SetButton(TextButton, commands.Contains(Directive) || commands.Contains(Speak));
        VolumeRow.Visibility = Visible(volume is not null || mute is not null || TextButton.Visibility == Visibility.Visible);
        Volume.Visibility = Visible(volume is not null);
        VolumeLabel.Visibility = Visible(!embedded && volume is not null);
        Volume.IsEnabled = volume is not null && CanInteract && DeviceExperience.CanWrite(context.Device, session.State, volume, null);
        if (!volumeDirty && volume is not null)
        {
            var valid = DeviceCardPresentation.TryValue(session.State, volume, out var value) && value.ValueKind == JsonValueKind.Number;
            painting = true;
            if (valid) Volume.Value = value.GetDouble();
            VolumeLabel.Text = valid ? VolumeText(Volume.Value) : "—";
            painting = false;
        }
        var muted = mute is not null && DeviceCardPresentation.TryValue(session.State, mute, out var current) && current.ValueKind == JsonValueKind.True;
        MuteIcon.Glyph = muted ? "\uE74F" : "\uE767";
        SetLabel(MuteButton, muted ? "SpeakerUnmute" : "SpeakerMute");
        SetButton(MuteButton, mute is not null, mute is not null && DeviceExperience.CanWrite(context.Device, session.State, mute, !muted));
        UpdateSend();
    }

    private void SetButton(Button button, bool visible, bool enabled = true)
    { button.Visibility = Visible(visible); button.IsEnabled = visible && enabled && CanInteract; }
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static void SetLabel(Button button, string key)
    { var label = UiText.Get(key); AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); }

    private string VolumeText(double value) => value.ToString("0.#") + (volume?.Unit == "percentage" ? "%" : "");
    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (session is null || painting || !CanInteract) return;
        volumeDirty = true;
        VolumeLabel.Text = VolumeText(args.NewValue);
        if (!dragging) { volumeTimer.Stop(); volumeTimer.Start(); }
    }
    private void CancelVolume() { volumeTimer.Stop(); dragging = volumeDirty = false; UpdateControls(); }
    private async Task SubmitVolumeAsync()
    {
        volumeTimer.Stop();
        if (!volumeDirty || volume is null) return;
        volumeDirty = false;
        var value = Volume.Value;
        if (!CanInteract || !AdapterControlValues.IsValid(volume, value)) { UpdateControls(); return; }
        object desired = volume.Type is "float" or "double" ? (object)value : (long)value;
        await WriteAsync(volume, desired);
    }
    private async void OnMute(object sender, RoutedEventArgs args)
    {
        if (mute is not null && DeviceCardPresentation.TryValue(session.State, mute, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            await WriteAsync(mute, !value.GetBoolean());
    }
    private async Task WriteAsync(PropertySchema property, object value)
    {
        if (!CanInteract) return;
        ShowFeedback(null);
        try { await session.WriteAsync(property, value); ShowFeedback(session.Feedback); }
        catch (BridgeException error) when (error.Code == "auth") { AuthenticationLost?.Invoke(); }
        catch (Exception error) { ShowFeedback(error.Message); }
    }

    private async void OnPrevious(object sender, RoutedEventArgs args) => await SendAsync("previous");
    private async void OnPlayPause(object sender, RoutedEventArgs args) => await SendAsync(playPauseCommand);
    private async void OnNext(object sender, RoutedEventArgs args) => await SendAsync("next");
    private void OnTextOpened(object sender, object args)
    {
        DirectiveMode.Visibility = Visible(commands.Contains(Directive));
        SpeakMode.Visibility = Visible(commands.Contains(Speak));
        TextMode.SelectedItem = commands.Contains(Directive) ? DirectiveMode : SpeakMode;
        UpdateSend();
        TextInput.Focus(FocusState.Programmatic);
    }
    private void OnTextModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => UpdateSend();
    private void OnTextChanged(object sender, TextChangedEventArgs args) => UpdateSend();
    private void UpdateSend()
    {
        if (session is null) return;
        Quiet.Visibility = Visible(TextMode.SelectedItem == DirectiveMode);
        SendText.IsEnabled = CanInteract && !string.IsNullOrWhiteSpace(TextInput.Text) && TextMode.SelectedItem is not null;
    }
    private async void OnSendText(object sender, RoutedEventArgs args)
    {
        var text = TextInput.Text.Trim();
        var directive = TextMode.SelectedItem == DirectiveMode;
        var quiet = directive && Quiet.IsChecked == true;
        if (!CanInteract || text.Length == 0) return;
        ClearDraft();
        await SendAsync(directive ? Directive : Speak, text, quiet);
    }
    private async Task SendAsync(string command, string? text = null, bool quiet = false)
    {
        if (!CanInteract || !commands.Contains(command) || context.Service is not ISpeakerActionService service) return;
        sending = true;
        ShowFeedback(null);
        UpdateControls();
        try
        {
            var result = await service.SendSpeakerActionAsync(new(context.Device.Id, context.Device.HomeId, command, Guid.NewGuid().ToString("N"), text, quiet));
            if (result.Status != "accepted") ShowFeedback(UiText.Get("SpeakerRejected"));
            else if (text is not null) ShowFeedback(UiText.Get("SpeakerAccepted"));
            else if (canInteract()) { await Task.Delay(500); if (canInteract()) await session.RefreshAsync(); }
        }
        catch (BridgeException error) when (error.Code == "auth") { AuthenticationLost?.Invoke(); }
        catch (BridgeException error) when (error.Code == "actionUnknown") { ShowFeedback(UiText.Get("SpeakerUnknown")); }
        catch (Exception error) { ShowFeedback(error.Message); }
        finally { sending = false; UpdateControls(); }
    }
    private void ShowFeedback(string? message)
    {
        Feedback.Text = UiText.State(message ?? "");
        Feedback.Visibility = Visible(Feedback.Text.Length != 0);
        ToolTipService.SetToolTip(Feedback, Feedback.Text);
    }
}
