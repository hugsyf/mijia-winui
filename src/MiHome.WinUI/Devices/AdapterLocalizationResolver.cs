using MiHome.WinUI.Core;
using MiHome.WinUI.Infrastructure;
namespace MiHome.WinUI.Devices;

internal static class AdapterLocalizationResolver
{
    public static string Label(DeviceAdapter adapter,string? key,string fallback)
    {
        if(key is null || adapter.Localization is not {} localization)return fallback;
        var requested=UiText.Language;
        foreach(var locale in new[]{requested,requested.Split('-')[0],localization.DefaultLocale}.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entries=localization.Resources.FirstOrDefault(pair=>pair.Key.Equals(locale,StringComparison.OrdinalIgnoreCase)).Value;
            if(entries is not null&&entries.TryGetValue(key,out var label))return label;
        }
        return fallback;
    }
    public static AdapterWidget Widget(DeviceAdapter adapter,AdapterWidget widget) => widget with
    {
        Label=Label(adapter,widget.LabelKey,widget.Label),
        Options=widget.Options?.Select(option=>option with{Label=Label(adapter,option.LabelKey,option.Label)}).ToList(),
        States=widget.States?.Select(state=>state with{Label=Label(adapter,state.LabelKey,state.Label)}).ToList(),
        Children=widget.Children?.Select(child=>Widget(adapter,child)).ToList()
    };
}
