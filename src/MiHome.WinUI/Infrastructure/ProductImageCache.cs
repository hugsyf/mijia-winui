using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace MiHome.WinUI.Infrastructure;

internal sealed class ProductImageCache(string directory, HttpMessageHandler? handler = null)
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private readonly HttpClient client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly SemaphoreSlim slots = new(3);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> pending = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> failures = new();

    public static bool Trusted(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        new[] { ".fds.api.xiaomi.com", ".fds.api.mi-img.com", ".fds.api.io.mi.com" }.Any(suffix => uri.DnsSafeHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public async Task<string?> GetAsync(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !Trusted(uri)) return null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address))).ToLowerInvariant();
        var path = Path.Combine(directory, key + ".img");
        if (File.Exists(path)) return path;
        if (failures.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow) return null;
        var task = pending.GetOrAdd(key, _ => new(() => DownloadAsync(uri, key, path))).Value;
        try { return await task; }
        finally { pending.TryRemove(key, out _); }
    }
    private async Task<string?> DownloadAsync(Uri uri, string key, string path)
    {
        await slots.WaitAsync();
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes) return Failed(key);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream(); var buffer = new byte[16384];
            while (true)
            {
                var length = await stream.ReadAsync(buffer, timeout.Token);
                if (length == 0) break;
                if (bytes.Length + length > MaximumBytes) return Failed(key);
                bytes.Write(buffer, 0, length);
            }
            var data = bytes.ToArray();
            if (!SupportedImage(data)) return Failed(key);
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, data); File.Move(temporary, path, true);
            return path;
        }
        catch { return Failed(key); }
        finally { slots.Release(); }
    }
    private string? Failed(string key) { failures[key] = DateTimeOffset.UtcNow.AddMinutes(10); return null; }
    internal static bool SupportedImage(byte[] data)
    {
        static bool Dimensions(int width, int height) => width is > 0 and <= 4096 && height is > 0 and <= 4096;
        if (data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return Dimensions(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4)));
        if (data.Length < 4 || data[0] != 255 || data[1] != 216) return false;
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position++] != 255) return false;
            while (position < data.Length && data[position] == 255) position++;
            if (position + 3 > data.Length) return false;
            var marker = data[position++];
            if (marker is 217 or 218) return false;
            if (marker is >= 208 and <= 215 || marker == 1) continue;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position, 2));
            if (length < 2 || position + length > data.Length) return false;
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
                return length >= 8 && Dimensions(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 3, 2)));
            position += length;
        }
        return false;
    }
}
