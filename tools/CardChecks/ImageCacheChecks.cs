using System.Net;
using System.Net.Http;
using MiHome.WinUI.Devices;
using MiHome.WinUI.Infrastructure;

internal static class ImageCacheChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mihome-cache-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        try
        {
            const string address = "https://cnbj1.fds.api.xiaomi.com/iotweb-product-center/example.png";
            foreach (var blocked in new[] { "http://cnbj1.fds.api.xiaomi.com/a", "https://cnbj1.fds.api.xiaomi.com.evil.example/a", "https://user:pass@cnbj1.fds.api.xiaomi.com/a", "https://cnbj1.fds.api.xiaomi.com:444/a" })
                Check(!ProductImageCache.Trusted(new Uri(blocked)), "Only the approved public HTTPS CDN is accepted");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");
            var transport = new FakeImages(png);
            var cache = new ProductImageCache(directory, transport);
            var requests = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => cache.GetAsync(address)));
            Check(requests.All(path => path == requests[0] && File.Exists(path)) && transport.Requests == 1, "Repeated image requests share one download");
            Check(File.ReadAllBytes(requests[0]!).SequenceEqual(png), "The validated image is cached without modification");
            var offline = new FakeImages(png) { Fail = true };
            Check(await new ProductImageCache(directory, offline).GetAsync(address) == requests[0] && offline.Requests == 0, "The cached image works offline");
            var invalid = new FakeImages("<html>not an image</html>"u8.ToArray());
            var invalidCache = new ProductImageCache(directory, invalid);
            Check(await invalidCache.GetAsync(address + "-invalid") is null && await invalidCache.GetAsync(address + "-invalid") is null && invalid.Requests == 1, "Invalid content is rejected and failures are throttled");
            var redirect = new FakeImages(png) { Redirect = true };
            Check(await new ProductImageCache(directory, redirect).GetAsync(address + "-redirect") is null, "Redirects are not accepted as image content");
            var large = new FakeImages(new byte[2 * 1024 * 1024 + 1]);
            Check(await new ProductImageCache(directory, large).GetAsync(address + "-large") is null, "Oversized payloads are rejected");
            var dimensions = (byte[])png.Clone(); dimensions[16] = 127;
            Check(!ProductImageCache.SupportedImage(dimensions), "Excessive decoded dimensions are rejected");
            Check(!DrawerLayout.Inline(1400, "inline", false) && DrawerLayout.Width(360, false) == 360 && DrawerLayout.Width(1400, false) == 400, "Details always use a responsive overlay");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"theme\":2,\"accent\":3,\"compactCards\":true,\"drawerMode\":\"inline\",\"refreshSeconds\":60}");
            var preferences = AppPreferences.Load(directory);
            Check(preferences is { Theme: 2, RefreshSeconds: 60, Accent: 0, CompactCards: false, DrawerMode: "overlay" }, "Legacy settings preserve theme and refresh while removing obsolete overrides");
            Console.WriteLine($"Passed {checks} targeted image-cache and preference checks; no real network or UI.");
        }
        finally
        {
            var target=Path.GetFullPath(directory);
            var expectedParent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Directory.GetParent(target)?.FullName!=expectedParent || !Path.GetFileName(target).StartsWith("mihome-cache-check-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected cleanup path");
            Directory.Delete(target,true);
        }
    }
    private sealed class FakeImages(byte[] bytes) : HttpMessageHandler
    {
        public int Requests;
        public bool Fail;
        public bool Redirect;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests); await Task.Delay(20, cancellationToken);
            if (Fail) throw new HttpRequestException("Offline");
            return new(Redirect ? HttpStatusCode.Redirect : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
