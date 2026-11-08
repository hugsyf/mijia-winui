using System.Globalization;
using System.Text.Json;
namespace MiHome.WinUI.Infrastructure;

// Both native XAML resources and this pure-.NET formatter are generated from catalog.json.
internal static class UiText
{
    public static string Language {get;private set;}=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"en-US":"zh-CN";
    private static readonly Dictionary<string,Dictionary<string,string>> resources=[];
    public static void Configure(string language)=>Language=language is "zh-CN" or "en-US"?language:CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"en-US":"zh-CN";
    private static Dictionary<string,string> Load(string language)
    {
        if(resources.TryGetValue(language,out var existing))return existing;
        using var input=typeof(UiText).Assembly.GetManifestResourceStream("MiHome.Ui."+language+".json");
        return resources[language]=input is null?[]:JsonSerializer.Deserialize<Dictionary<string,string>>(input)??[];
    }
    public static string Get(string key)=>Load(Language).GetValueOrDefault(key)??Load("zh-CN").GetValueOrDefault(key)??key;
    public static string Format(string key,params object?[] values)=>string.Format(CultureInfo.CurrentCulture,Get(key),values);
    public static string State(string value)
    {
        if(Language=="zh-CN"||value.Length==0)return value;
        var source=Load("zh-CN");var target=Load(Language);
        var match=source.FirstOrDefault(pair=>pair.Value==value);
        return match.Key is not null?target.GetValueOrDefault(match.Key,value):value;
    }
    public static string EnumLabel(string value)=>value.ToLowerInvariant() switch
    {"stop" or "stopped"=>Get("Stopped"),"play" or "playing"=>Get("Playing"),"pause" or "paused"=>Get("Paused"),_=>value};
}
