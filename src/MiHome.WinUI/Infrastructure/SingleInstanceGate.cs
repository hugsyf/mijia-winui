using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;

namespace MiHome.WinUI.Infrastructure;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource cancellation = new();
    private readonly string pipeName;
    private bool ownsMutex;
    private SingleInstanceGate()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var scope = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..20];
        pipeName = $"MiHomeWinUI-{scope}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        mutex = new Mutex(false, @"Local\" + pipeName);
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
    }
    public static async Task<SingleInstanceGate?> StartAsync(Action activate, bool startup)
    {
        var gate = new SingleInstanceGate();
        if (gate.ownsMutex)
        {
            _ = gate.ListenAsync(activate);
            return gate;
        }
        try
        {
            using var client = new NamedPipeClientStream(".", gate.pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(5000);
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId)) AllowSetForegroundWindow(processId);
            await client.WriteAsync(new byte[] { startup ? (byte)0 : (byte)1 });
            await client.FlushAsync();
        }
        finally { gate.Dispose(); }
        return null;
    }
    private async Task ListenAsync(Action activate)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellation.Token);
                var command = new byte[1];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                if (await server.ReadAsync(command, timeout.Token) == 1 && command[0] == 1) activate();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { if (!cancellation.IsCancellationRequested) await Task.Delay(250); }
        }
    }
    public void Dispose()
    {
        cancellation.Cancel();
        if (ownsMutex) { mutex.ReleaseMutex(); ownsMutex = false; }
        mutex.Dispose();
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
