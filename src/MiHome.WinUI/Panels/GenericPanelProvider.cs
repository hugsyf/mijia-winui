using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Controls;
namespace MiHome.WinUI.Panels;

public sealed class GenericPanelProvider : IDevicePanelProvider
{
    public string Id => "builtin.generic";
    public int ContractVersion => 1;
    public int Priority => 0;
    public PanelMatch Match(DeviceIdentity device, DeviceSchema schema) => PanelMatch.Generic;
    public FrameworkElement? CreateCard(PanelContext context) => null;
    public FrameworkElement CreateDetail(PanelContext context) => new GenericPanel(context);
}
