using System.Text.Json;
namespace MiHome.WinUI.Infrastructure;

internal sealed record AppPreferences(int Theme = 0, int Accent = 0, bool CompactCards = false,
    string DrawerMode = "overlay", int RefreshSeconds = 30, string Language="system", bool CheckUpdates=true,
    bool StartHidden=false, bool CloseToTray=false)
{
    private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static AppPreferences Load(string directory)
    {
        try
        {
            var value = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(Path.Combine(directory, "settings.json")), json)!;
            return value with { Language=value.Language is "zh-CN" or "en-US"?value.Language:"system", Theme = Math.Clamp(value.Theme, 0, 2), Accent = 0, CompactCards = false,
                DrawerMode = "overlay",
                RefreshSeconds = value.RefreshSeconds is 0 or 10 or 30 or 60 ? value.RefreshSeconds : 30 };
        }
        catch { return new(); }
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, json));
        File.Move(file + ".tmp", file, true);
    }
}
