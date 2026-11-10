using Microsoft.UI.Xaml;
using MiHome.WinUI.Core;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Panels;

namespace MiHome.WinUI;

public sealed partial class QuickControlsWindow
{
    private DeviceIdentity? traySpeaker;
    private SpeakerPanel? speakerPanel;
    private bool speakerLoading, speakerUnavailable, savingSpeaker;
    private int speakerRevision;

    private void UpdateTraySpeaker(bool editing, bool busy)
    {
        var candidates = session.Available.Where(tile => SpeakerPanel.IsCandidate(tile.Device)).ToArray();
        var show = session.Authenticated && candidates.Length != 0 && !editing && selected is null;
        SpeakerSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var target = candidates.FirstOrDefault(tile => tile.Device.Id == session.TraySpeakerId)
            ?? candidates.FirstOrDefault(tile => tile.Device.Id == traySpeaker?.Id)
            ?? candidates.FirstOrDefault(tile => tile.Device.Online) ?? candidates.FirstOrDefault();
        if (traySpeaker != target?.Device)
        {
            speakerRevision++;
            if (speakerPanel is { } previous)
            {
                previous.SpeakerChanged -= OnSpeakerChanged;
                previous.ClearDraft();
            }
            speakerLoading = speakerUnavailable = false;
            traySpeaker = target?.Device;
            speakerPanel = target is null ? null : new SpeakerPanel(embedded: true);
            if (speakerPanel is { } current) current.SpeakerChanged += OnSpeakerChanged;
            SpeakerHost.Content = speakerPanel;
        }
        if (target is null || speakerPanel is not { } panel) return;
        panel.SetTrayIdentity(target, candidates.Select(tile => tile.Device).ToArray(), !busy && !savingSpeaker);
        ShowSpeakerStatus();
        panel.UpdateControls();
        if (show && !busy && IsOpen && !panel.IsReady && !speakerLoading && !speakerUnavailable)
            _ = SafeAsync(RefreshTraySpeakerAsync);
    }

    private bool CanUseTraySpeaker(DeviceIdentity device, int revision) => IsOpen && !closed && speakerRevision == revision &&
        session.Authenticated && !session.MainBusy && !session.Busy && !savingSpeaker && !modal && Edit.IsChecked != true &&
        selected is null && session.Home?.Id == device.HomeId;

    private async Task RefreshQuickAsync()
    {
        await session.RefreshAsync();
        await RefreshTraySpeakerAsync();
    }

    private async Task RefreshTraySpeakerAsync()
    {
        var revision = speakerRevision;
        if (traySpeaker is not { } device || speakerPanel is not { } panel || speakerLoading || !CanUseTraySpeaker(device, revision)) return;
        if (panel.IsReady) { await panel.RefreshAsync(); return; }
        speakerLoading = true;
        speakerUnavailable = false;
        ShowSpeakerStatus();
        try
        {
            var service = session.QuickService(device.Id);
            var schema = await service.GetSchemaAsync(device.Id);
            if (speakerRevision != revision || closed || !IsOpen) return;
            var context = new PanelContext(device, schema, service);
            if (!SpeakerPanel.Supports(context)) { speakerUnavailable = true; return; }
            var initial = session.Available.FirstOrDefault(tile => tile.Device.Id == device.Id)?.State;
            panel.Configure(context, initial, () => CanUseTraySpeaker(device, revision));
            panel.StateRead += session.CapturePublisher();
            await panel.RefreshAsync();
        }
        catch (Exception) { if (speakerRevision == revision) speakerUnavailable = true; }
        finally
        {
            if (speakerRevision == revision)
            {
                speakerLoading = false;
                ShowSpeakerStatus();
            }
        }
    }

    private void ShowSpeakerStatus()
    {
        speakerPanel?.SetPendingStatus(UiText.Get(speakerUnavailable ? "SpeakerUnavailable" : "SpeakerLoading"));
    }

    private async void OnSpeakerChanged(DeviceIdentity device)
    {
        if (rendering || savingSpeaker || device.Id == traySpeaker?.Id) return;
        savingSpeaker = true;
        speakerPanel?.ClearDraft();
        UpdateView();
        try { await SafeAsync(() => session.SaveTraySpeakerAsync(device.Id)); }
        finally { savingSpeaker = false; UpdateView(); }
    }

    private void OnTraySectionSizeChanged(object sender, SizeChangedEventArgs args)
    { if (IsOpen) Position(); }
}
