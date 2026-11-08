using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using MiHome.WinUI.Infrastructure;
using MiHome.WinUI.Core;
using MiHome.WinUI.Devices;
using MiHome.WinUI.ViewModels;
using MiHome.WinUI.Panels;

internal static class UpdateAndLocaleChecks
{
    public static async Task RunAsync()
    {
        var checks=0;
        void Check(bool valid,string name){checks++;if(!valid)throw new Exception(name);}
        Check(UpdateService.IsNewer("v0.19.0",new Version(0,18,0,0)),"Normalize release versions");
        Check(!UpdateService.IsNewer("0.19.0",new Version(0,19,0,0)),"Equal versions are current");
        Check(UpdateService.ParseVersion("v0.20.0-beta.1") is null,"Do not treat pre-release as stable");
        Check(!UpdateService.ReleasePage(new Uri("https://github.com/other/repository/releases/tag/v1")),"Repository boundary");
        Check(!UpdateService.ReleasePage(new Uri("http://github.com/hugsyf/mijia-winui/releases/tag/v1")),"HTTPS boundary");
        var payload=new byte[]{1,2,3,4};var hash=Convert.ToHexString(SHA256.HashData(payload));
        var releaseJson=JsonSerializer.SerializeToElement(new{tag_name="v0.19.0",html_url="https://github.com/hugsyf/mijia-winui/releases/tag/v0.19.0",body="Notes",draft=false,prerelease=false,assets=new[]{new{name="MiHomeWinUI-0.19.0-x64-Setup.exe",size=4,browser_download_url="https://github.com/hugsyf/mijia-winui/releases/download/v0.19.0/MiHomeWinUI-0.19.0-x64-Setup.exe",digest="sha256:"+hash}}});
        var release=UpdateService.Parse(releaseJson);
        Check(release.Download is not null&&release.Size==4,"Select exact installer and digest");
        var folder=Path.Combine(Path.GetTempPath(),"mihome-update-"+Guid.NewGuid());
        try
        {
            var service=new UpdateService(folder,new Handler(payload));
            var path=await service.DownloadAsync(release,null,default);
            Check(File.ReadAllBytes(path).SequenceEqual(payload),"Verified download");
            try{await service.DownloadAsync(release with{Sha256=new string('0',64)},null,default);throw new Exception("Corrupt package accepted");}
            catch(InvalidDataException){Check(!Directory.EnumerateFiles(folder,"*.partial").Any(),"Delete failed partial download");}
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try{await service.DownloadAsync(release,null,cancelled.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){checks++;}
            var missing=new StatusHandler(HttpStatusCode.NotFound);
            var checker=new UpdateService(Path.Combine(folder,"missing"),missing);
            Check(await checker.CheckAsync(true) is null,"No public release is distinct from current version");
            Check(await checker.CheckAsync(true) is null&&missing.Calls==1,"Automatic checks reuse daily cache");
            var limited=new StatusHandler(HttpStatusCode.Forbidden);
            checker=new UpdateService(Path.Combine(folder,"limited"),limited);
            for(var i=0;i<2;i++)try{await checker.CheckAsync(true);throw new Exception("Rate-limit failure treated as success");}catch(HttpRequestException){}
            Check(limited.Calls==1,"Failed automatic checks also have a daily cooldown");
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        UiText.Configure("zh-CN");Check(PropertyPresentation.Unit("minutes")=="分钟","Localized time unit");
        UiText.Configure("en-US");Check(PropertyPresentation.Unit("minutes")=="min","English time unit");
        var property=new PropertySchema(2,1,"play-control","Playback","uint8","",true,false,[],[new(JsonSerializer.SerializeToElement(0),"Stop")]){PropertyType="urn:miot-spec-v2:property:playing-state:1:1"};
        UiText.Configure("zh-CN");Check(PropertyPresentation.EnumLabel(property,property.Values[0])=="已停止","Contextual stop enum");
        var adapter=new DeviceAdapter("example.test","Example","1.0.0","light",[],[],[],[],""){Localization=new("zh-CN",new(){["zh-CN"]=new(){["level"]="亮度"},["en-US"]=new(){["level"]="Brightness"}})};
        UiText.Configure("en-US");Check(AdapterLocalizationResolver.Label(adapter,"level","Fallback")=="Brightness","Plugin locale selection");
        Check(AdapterLocalizationResolver.Label(adapter,"absent","Fallback")=="Fallback","Missing key fallback");
        UiText.Configure("zh-CN");Check(AdapterLocalizationResolver.Label(adapter,"level","Fallback")=="亮度","Chinese plugin locale");
        var condition=new AdapterCondition("power",JsonSerializer.SerializeToElement(true));
        Check(!AdapterConditions.Matches(adapter,null,condition),"Missing dependency stays disabled");
        Console.WriteLine($"Update, language, and condition checks passed: {checks}.");
    }
    private sealed class Handler(byte[] bytes):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {cancellation.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    }
    private sealed class StatusHandler(HttpStatusCode status):HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {Calls++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("")});}
    }
}
