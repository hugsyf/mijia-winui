using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MiHome.WinUI.Infrastructure;

internal sealed record UpdateRelease(string Version,string Page,string Notes,string? Download,string? Sha256,long Size);
internal sealed class UpdateService(string directory,HttpMessageHandler? handler=null)
{
    internal const string Repository="hugsyf/mijia-winui";
    private const long MaximumBytes=1024L*1024*1024;
    private readonly HttpClient client=new(handler??new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=TimeSpan.FromSeconds(20)};
    private static readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
    private sealed record Cache(DateTimeOffset Checked,string? ETag,string Json);
    private string CachePath=>Path.Combine(directory,"check.json");
    private string AttemptPath=>Path.Combine(directory,"attempt.json");
    public static Version? ParseVersion(string value)=>Version.TryParse(value.Trim().TrimStart('v','V'),out var version)?new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision)):null;
    public static bool IsNewer(string remote,Version current)=>ParseVersion(remote) is {} version && version>new Version(current.Major,current.Minor,Math.Max(0,current.Build),Math.Max(0,current.Revision));
    internal static bool ReleasePage(Uri uri)=>uri.Scheme=="https"&&uri.Host=="github.com"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.AbsolutePath.StartsWith("/"+Repository+"/releases/",StringComparison.Ordinal);
    private static bool DownloadHost(Uri uri)=>uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    internal static UpdateRelease Parse(JsonElement root)
    {
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        var tag=root.GetProperty("tag_name").GetString()!;
        if(ParseVersion(tag) is null)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        var page=root.GetProperty("html_url").GetString()!;
        if(!Uri.TryCreate(page,UriKind.Absolute,out var uri)||!ReleasePage(uri))throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        string? download=null,hash=null;long size=0;
        var name=$"MiHomeWinUI-{tag.TrimStart('v','V')}-x64-Setup.exe";
        foreach(var asset in root.GetProperty("assets").EnumerateArray())
        {
            if(asset.GetProperty("name").GetString()!=name)continue;
            var address=asset.GetProperty("browser_download_url").GetString();
            if(!Uri.TryCreate(address,UriKind.Absolute,out var source)||!ReleasePage(source)||!source.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal))continue;
            var digest=asset.TryGetProperty("digest",out var value)?value.GetString():null;
            size=asset.GetProperty("size").GetInt64();
            if(size<=0||size>MaximumBytes||digest is null||!Regex.IsMatch(digest,"^sha256:[a-fA-F0-9]{64}$"))continue;
            download=address;hash=digest[7..];break;
        }
        return new(tag,page,root.TryGetProperty("body",out var notes)?notes.GetString()??"":"",download,hash,size);
    }
    public async Task<UpdateRelease?> CheckAsync(bool automatic=false,CancellationToken cancellation=default)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(20));cancellation=deadline.Token;
        Cache? cached=null;
        try {if(File.Exists(CachePath)&&new FileInfo(CachePath).Length<1024*1024)cached=JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(CachePath,cancellation),json);}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){}
        if(automatic&&cached is not null&&DateTimeOffset.UtcNow-cached.Checked<TimeSpan.FromDays(1))return cached.Json.Length==0?null:Parse(JsonDocument.Parse(cached.Json).RootElement);
        DateTimeOffset? attempted=null;
        try {if(File.Exists(AttemptPath))attempted=JsonSerializer.Deserialize<DateTimeOffset>(await File.ReadAllTextAsync(AttemptPath,cancellation));}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){}
        if(automatic&&attempted is {} last&&DateTimeOffset.UtcNow-last<TimeSpan.FromDays(1))throw new HttpRequestException(UiText.Get("UpdateFailed"));
        try {Directory.CreateDirectory(directory);await File.WriteAllTextAsync(AttemptPath,JsonSerializer.Serialize(DateTimeOffset.UtcNow),cancellation);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/"+Repository+"/releases/latest");
        request.Headers.UserAgent.ParseAdd("MiHomeWinUI/0.21.0");request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if(cached?.ETag is {} etag)request.Headers.TryAddWithoutValidation("If-None-Match",etag);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new HttpRequestException(UiText.Get("UpdateLimited"));
        string data;
        if(response.StatusCode==HttpStatusCode.NotModified&&cached is not null)data=cached.Json;
        else if(response.StatusCode==HttpStatusCode.NotFound)data="";
        else
        {
            response.EnsureSuccessStatusCode();
            var bytes=await ReadBoundedAsync(response,1024*1024,cancellation);
            data=System.Text.Encoding.UTF8.GetString(bytes);
        }
        UpdateRelease? release=data.Length==0?null:Parse(JsonDocument.Parse(data).RootElement);
        try {Directory.CreateDirectory(directory);await File.WriteAllTextAsync(CachePath,JsonSerializer.Serialize(new Cache(DateTimeOffset.UtcNow,response.Headers.ETag?.ToString()??cached?.ETag,data),json),cancellation);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        return release;
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response,int maximum,CancellationToken cancellation)
    {
        if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        using var input=await response.Content.ReadAsStreamAsync(cancellation);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,cancellation))>0){if(output.Length+count>maximum)throw new InvalidDataException(UiText.Get("UpdateInvalid"));await output.WriteAsync(buffer.AsMemory(0,count),cancellation);}
        return output.ToArray();
    }
    public async Task<string> DownloadAsync(UpdateRelease release,IProgress<double>? progress,CancellationToken cancellation)
    {
        if(release.Download is null||release.Sha256 is null||release.Size<=0||release.Size>MaximumBytes)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        var address=new Uri(release.Download);
        if(!ReleasePage(address))throw new InvalidDataException(UiText.Get("UpdateInvalid"));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromMinutes(30));
        for(var redirect=0;redirect<5;redirect++)
        {
            if(!DownloadHost(address))throw new InvalidDataException(UiText.Get("UpdateInvalid"));
            using var request=new HttpRequestMessage(HttpMethod.Get,address);request.Headers.UserAgent.ParseAdd("MiHomeWinUI/0.21.0");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {if(response.Headers.Location is not {} location)break;address=location.IsAbsoluteUri?location:new Uri(address,location);continue;}
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is {} length&&length!=release.Size)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
            Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".partial");
            try
            {
                using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                await using(var input=await response.Content.ReadAsStreamAsync(deadline.Token))
                {
                    var buffer=new byte[65536];long total=0;int count;
                    while((count=await input.ReadAsync(buffer,deadline.Token))>0)
                    {
                        total+=count;if(total>release.Size)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
                        digest.AppendData(buffer,0,count);await file.WriteAsync(buffer.AsMemory(0,count),deadline.Token);progress?.Report(total*100d/release.Size);
                    }
                    if(total!=release.Size)throw new InvalidDataException(UiText.Get("UpdateInvalid"));
                }
                if(!Convert.ToHexString(digest.GetHashAndReset()).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.Get("UpdateHashFailed"));
                var target=Path.Combine(directory,$"MiHomeWinUI-{ParseVersion(release.Version)}-x64-Setup.exe");File.Move(temporary,target,true);return target;
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        throw new InvalidDataException(UiText.Get("UpdateInvalid"));
    }
}
