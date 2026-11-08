using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Infrastructure;

public sealed class BackendClient : IDeviceService, IDeviceActionService, IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim inputLock = new(1);
    private Process? process;
    private IntPtr job;
    private long nextId;
    public event Action<JsonElement>? EventReceived;
    public event Action? Disconnected;
    public bool Running => process is { HasExited: false };

    public async Task StartAsync()
    {
        Dispose();
        var root = AppContext.BaseDirectory;
        var python = Path.Combine(root, "runtime", "python.exe");
        var worker = Path.Combine(root, "backend", "worker.py");
        if (!File.Exists(python)) throw new Exception("程序文件不完整，请重新安装米家。");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = root };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(worker);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["MIHOME_UI_LANGUAGE"]=UiText.Language;
        process = Process.Start(start) ?? throw new Exception("连接服务无法启动，请重新打开应用。");
        try { job = ProcessJob.Attach(process); }
        catch { process.Kill(true); throw; }
        var owned = process;
        _ = Task.Run(async () =>
        {
            try
            {
                while (await owned.StandardOutput.ReadLineAsync() is { } line)
                {
                    using var doc = JsonDocument.Parse(line);
                    var message = doc.RootElement.Clone();
                    if (message.GetProperty("version").GetInt32() != 1) throw new Exception("通信版本不兼容。");
                    if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && pending.TryRemove(id.GetInt64(), out var completion))
                    {
                        if (message.TryGetProperty("error", out var error))
                            completion.TrySetException(new BridgeException(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!));
                        else completion.TrySetResult(message.GetProperty("result").Clone());
                    }
                    else if (message.TryGetProperty("event", out _)) EventReceived?.Invoke(message);
                }
            }
            catch (Exception) { /* Fail all pending callers with a safe message below. */ }
            finally
            {
                if (ReferenceEquals(process, owned))
                {
                    foreach (var item in pending.ToArray())
                        if (pending.TryRemove(item.Key, out var tcs)) tcs.TrySetException(new Exception("连接已中断，请重新连接。"));
                    Disconnected?.Invoke();
                }
            }
        });
        // Never copy Python stderr to UI/logs: upstream exceptions may contain secrets.
        _ = Task.Run(async () => { try { while (await owned.StandardError.ReadLineAsync() is not null) { } } catch { } });
        var hello = await CallAsync("Hello");
        if (hello.GetProperty("protocol").GetInt32() != 1) throw new Exception("通信版本不兼容，请重新安装。");
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters = null)
    {
        var owned = process;
        if (owned is null || owned.HasExited) throw new Exception("尚未连接米家，请重新连接。");
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await inputLock.WaitAsync();
            try
            {
                await owned.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { version = 1, id, method, @params = parameters }, Json));
                await owned.StandardInput.FlushAsync();
            }
            finally { inputLock.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
        }
        catch (TimeoutException)
        {
            if (method == "RunAction") throw new BridgeException("actionUnknown", "动作结果待确认，请检查设备状态，不要重复提交。");
            throw new Exception(method == "WriteProperty" ? "操作超时，结果待确认。请刷新状态，不要重复提交。" : "请求超时，请稍后刷新。");
        }
        finally { pending.TryRemove(id, out _); }
    }
    public async Task<DeviceSchema> GetSchemaAsync(string deviceId) => (await CallAsync("GetDeviceSchema", new { deviceId })).Deserialize<DeviceSchema>(Json)!;
    public Task<JsonElement> ReadAsync(string deviceId) => CallAsync("ReadProperties", new { deviceId });
    public async Task WriteAsync(PropertyKey key, object? value) => await CallAsync("WriteProperty", new { deviceId = key.DeviceId, siid = key.Siid, piid = key.Piid, value });
    public async Task<ActionResult> InvokeActionAsync(ActionRequest request) => (await CallAsync("RunAction", request)).Deserialize<ActionResult>(Json)!;
    public void Dispose()
    {
        var owned = process;
        process = null;
        if (owned is not null)
        {
            try
            {
                if (!owned.HasExited)
                {
                    owned.StandardInput.WriteLine("{\"version\":1,\"id\":0,\"method\":\"Shutdown\"}");
                    owned.StandardInput.Flush();
                    if (!owned.WaitForExit(500)) owned.Kill(true);
                }
            }
            catch { try { owned.Kill(true); } catch { } }
            owned.Dispose();
        }
        if (job != IntPtr.Zero) { ProcessJob.CloseHandle(job); job = IntPtr.Zero; }
        foreach (var item in pending.ToArray()) if (pending.TryRemove(item.Key, out var tcs)) tcs.TrySetException(new Exception("连接已关闭。"));
    }
}

internal static class ProcessJob
{
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int type, ref Extended info, uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static IntPtr Attach(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var limits = new Extended { Basic = new Basic { Flags = 0x2000 } };
        if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(job, process.Handle))
        { if (job != IntPtr.Zero) CloseHandle(job); throw new Exception("连接服务无法启动，请重新打开应用。"); }
        return job;
    }
}
