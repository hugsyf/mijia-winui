using MiHome.WinUI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
namespace MiHome.WinUI.ViewModels;

public sealed record DeviceTile(DeviceIdentity Device, DeviceCardState? State = null, bool IsEditing = false, bool CompactLayout = false)
{
    private Devices.LiveCardSnapshot snapshot = new(State);
    public DeviceCardState? State { get => snapshot.Value; init => snapshot = new(value); }
    public void UpdateSnapshot(DeviceCardState? state) => snapshot.Set(state);
    public event Action? SnapshotChanged { add => snapshot.Changed += value; remove => snapshot.Changed -= value; }
    public string? PresentationSize { get; init; }
    public string Size => PresentationSize is "compact" or "standard" or "expanded" ? PresentationSize : Device.CardSize is "compact" or "standard" or "expanded" ? Device.CardSize : CompactLayout ? "compact" : Devices.DeviceExperience.DefaultSize(Device);
    public string Name => Device.Name;
    public string Room => Device.Room;
    public string Category => DeviceCardPresentation.CategoryName(DeviceCardPresentation.Category(Device, State));
    public string Summary => DeviceCardPresentation.Summary(Device, State);
    public Visibility SummaryVisibility => Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Status => !Device.Online ? "离线或状态未知" : Summary == "状态暂不可用" ? "" : State?.Error ?? "";
    public Visibility StatusVisibility => Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string StateHint => State?.ReadAt is { } time
        ? Infrastructure.UiText.Format("ReadingTime",time.ToLocalTime().ToString("HH:mm"))
        : Infrastructure.UiText.Get("NoState");
    public string AccessibleName => $"{Name}，{Room}，{Category}，{Summary}，{Status}，打开设备详情";
    public Geometry IconGeometry => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),
        DeviceCardPresentation.IconPath(DeviceCardPresentation.Category(Device, State) == "wireless-switch" ? "wireless-switch" :
            DeviceCardPresentation.CanChooseAppearance(Device) && Device.CardIcon.Length > 0 ? Device.CardIcon : DeviceCardPresentation.Category(Device, State)));
}
