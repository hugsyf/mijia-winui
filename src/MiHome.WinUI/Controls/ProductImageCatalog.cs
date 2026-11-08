using System.Text.Json;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;

internal static class ProductImageCatalog
{
    private static readonly string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Devices");
    private sealed record Purpose(string Url, string Name, string PluginType, string Category);
    private static readonly Lazy<Dictionary<string, Purpose>> purposes = new(() =>
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"purpose-index.json")));
            return json.RootElement.GetProperty("records").Deserialize<Dictionary<string,Purpose>>(new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? [];
        }
        catch { return []; }
    });
    public static string? Find(DeviceTile tile) => tile.Device.Adapter is { IconFile.Length: > 0 } adapter && File.Exists(adapter.IconFile) ? adapter.IconFile : null;
    private static string? PurposeUrl(DeviceTile tile)
    {
        var category=DeviceCardPresentation.Category(tile.Device,tile.State);
        if(!DeviceCardPresentation.CanChooseAppearance(tile.Device)||category=="wireless-switch")return null;
        var group=tile.Device.Model=="cuco.plug.v3"?"socket":"switch";
        if(tile.Device.CardAppearance.Length==0)
            return purposes.Value.TryGetValue(tile.Device.SubclassId,out var known)&&known.PluginType==group?known.Url:null;
        return purposes.Value.Values.FirstOrDefault(p=>p.PluginType==group&&p.Category==category)?.Url;
    }
    private static readonly Lazy<Dictionary<string,string>> remote = new(() =>
    {
        try {return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(directory,"catalog.json")))??[];}
        catch {return [];}
    });
    private static readonly Infrastructure.ProductImageCache cache = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiHomeWinUI","Images"));
    public static async Task<string?> ResolveAsync(DeviceTile tile)
    {
        if(Find(tile) is {} local)return local;
        if(PurposeUrl(tile) is {} purpose && await cache.GetAsync(purpose) is {} image)return image;
        return remote.Value.TryGetValue(tile.Device.Model,out var url)?await cache.GetAsync(url):null;
    }
}
