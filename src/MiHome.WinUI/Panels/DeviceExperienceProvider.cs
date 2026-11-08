using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Panels;

public sealed class DeviceExperienceProvider : IDevicePanelProvider
{
    public string Id => "builtin.device-experience";
    public int ContractVersion => 1;
    public int Priority => 10;
    public PanelMatch Match(DeviceIdentity device, DeviceSchema schema) =>
        DeviceExperience.Kind(device, schema) != ExperienceKind.Generic && schema.Properties.Count > 0
            ? DeviceModels.Find(device) is not null ? PanelMatch.Model : PanelMatch.Capability : PanelMatch.None;
    public FrameworkElement? CreateCard(PanelContext context) => null;
    public FrameworkElement CreateDetail(PanelContext context) => new DeviceExperiencePanel(context);
}
