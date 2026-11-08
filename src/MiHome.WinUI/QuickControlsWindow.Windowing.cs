using System.Runtime.InteropServices;

namespace MiHome.WinUI;

public sealed partial class QuickControlsWindow
{
    private ForegroundEvent? foregroundCallback;
    private nint foregroundHook;
    private MouseEvent? mouseCallback;
    private nint mouseHook;

    private void ActivatePopup()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // Window.Activate alone does not transfer foreground ownership from Explorer.
        SetForegroundWindow(handle);
        if (foregroundHook == 0)
        {
            foregroundCallback ??= (_, _, _, _, _, _, _) => DispatcherQueue.TryEnqueue(CheckForeground);
            foregroundHook = SetWinEventHook(3, 3, 0, foregroundCallback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context.
        }
        if (mouseHook == 0)
        {
            mouseCallback ??= OnMouseInput;
            mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0); // WH_MOUSE_LL, only while visible.
        }
        DispatcherQueue.TryEnqueue(CheckForeground);
    }

    private void CheckForeground()
    {
        if (!IsOpen || closed) return;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var foreground = GetForegroundWindow();
        // Keep owned WinUI popups open, but dismiss when another window takes focus.
        if (BelongsToPopup(foreground, handle)) return;
        deactivatedAt = Environment.TickCount64;
        Dismiss();
    }

    private static bool BelongsToPopup(nint candidate, nint handle) =>
        candidate == handle || candidate != 0 && GetAncestor(candidate, 3) == handle;

    private nint OnMouseInput(int code, nuint message, nint data)
    {
        // Observe outside presses without consuming input or treating hover as dismissal.
        if (code >= 0 && IsOpen && message is 0x201 or 0x204 or 0x207 or 0x20b)
        {
            var point = new NativePoint { X = Marshal.ReadInt32(data), Y = Marshal.ReadInt32(data, 4) };
            if (!BelongsToPopup(WindowFromPoint(point), WinRT.Interop.WindowNative.GetWindowHandle(this)))
                DispatcherQueue.TryEnqueue(() => { if (IsOpen && !closed) { deactivatedAt = Environment.TickCount64; Dismiss(); } });
        }
        return CallNextHookEx(0, code, message, data);
    }

    private void StopForegroundWatch()
    {
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        mouseHook = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ForegroundEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint MouseEvent(int code, nuint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, ForegroundEvent callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] private static extern nint SetWindowsHookEx(int type, MouseEvent callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
