using Microsoft.UI.Xaml;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Panels;

public sealed class PanelRegistry
{
    public const int ContractVersion = 1;
    private readonly List<IDevicePanelProvider> providers = [];
    public void Register(IDevicePanelProvider provider)
    {
        if (provider.ContractVersion != ContractVersion || providers.Any(p => p.Id == provider.Id))
            throw new InvalidOperationException("设备面板版本或标识冲突。");
        providers.Add(provider);
    }
    public FrameworkElement Create(PanelContext context, IDevicePanelProvider fallback)
    {
        var ranked = providers.Select(p => (Provider: p, Match: SafeMatch(p, context)))
            .Where(x => x.Match != PanelMatch.None).OrderByDescending(x => x.Match).ThenByDescending(x => x.Provider.Priority).ToList();
        if (ranked.Count > 1 && ranked[0].Match == ranked[1].Match && ranked[0].Provider.Priority == ranked[1].Provider.Priority)
            throw new InvalidOperationException("设备面板匹配优先级冲突。");
        try { return ranked.FirstOrDefault().Provider?.CreateDetail(context) ?? fallback.CreateDetail(context); }
        catch { return fallback.CreateDetail(context); }
    }
    private static PanelMatch SafeMatch(IDevicePanelProvider provider, PanelContext context)
    {
        try { return provider.Match(context.Device, context.Schema); }
        catch { return PanelMatch.None; }
    }
}
