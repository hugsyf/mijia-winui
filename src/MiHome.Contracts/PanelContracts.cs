using Microsoft.UI.Xaml;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Panels;

public enum PanelMatch { None = 0, Generic = 1, Capability = 2, SpecType = 3, Model = 4 }
public sealed record PanelContext(DeviceIdentity Device, DeviceSchema Schema, IDeviceService Service);
public interface IDevicePanelProvider
{
    string Id { get; }
    int ContractVersion { get; }
    int Priority { get; }
    PanelMatch Match(DeviceIdentity device, DeviceSchema schema);
    FrameworkElement? CreateCard(PanelContext context);
    FrameworkElement CreateDetail(PanelContext context);
}
public interface IRefreshablePanel { Task RefreshAsync(); }
