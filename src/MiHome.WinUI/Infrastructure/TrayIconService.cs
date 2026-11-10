using System.Runtime.InteropServices;
using Windows.Graphics;

namespace MiHome.WinUI.Infrastructure;

internal sealed class TrayIconService : IDisposable
{
    private const uint Callback = 0x8001;
    // Unsigned notification icons are tied to their executable path by Windows.
    private readonly Guid guid = new(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("MiHomeWinUI:" + Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory).ToUpperInvariant())).AsSpan(0, 16));
    private readonly WindowProcedure procedure;
    private readonly string className = "MiHomeWinUI.Tray." + Environment.ProcessId;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint window, icon;
    public bool Ready { get; private set; }
    internal nint Handle => window;
    internal event Action<uint, nuint, nint>? WindowMessage;
    public event Action? Click;
    public event Action? ContextMenu;
    public event Action? DoubleClick;
    public event Action? Unavailable;
    public event Action? ShutdownRequested;
    public TrayIconService()
    {
        procedure = WndProc;
        var definition = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = GetModuleHandle(null), Name = className };
        if (RegisterClass(ref definition) == 0) throw new InvalidOperationException("Cannot register tray window.");
        window = CreateWindowEx(0x80, className, "", 0x80000000, 0, 0, 0, 0, 0, 0, definition.Instance, 0);
        icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 0, 0, 0x10 | 0x40);
        if (window == 0 || icon == 0) { Dispose(); throw new InvalidOperationException(UiText.Get("TrayUnavailable")); }
        AddIcon();
        if (!Ready) { Dispose(); throw new InvalidOperationException(UiText.Get("TrayUnavailable")); }
    }
    private IconData Data() => new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = window, Id = 1,
        Flags = 1 | 2 | 4 | 0x20 | 0x80, Message = Callback, Icon = icon, Tip = "米家 WinUI", Guid = guid, Info = "", InfoTitle = "" };
    private void AddIcon()
    {
        var data = Data(); Ready = ShellNotifyIcon(0, ref data);
        if (Ready) { data.Version = 4; ShellNotifyIcon(4, ref data); }
    }
    private nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message is 0x0312 or 0x02B1 or 0x0218) WindowMessage?.Invoke(message, wParam, lParam);
            if (message == 0x11) return 1; // WM_QUERYENDSESSION: never block sign-out or an installer shutdown.
            if (message == 0x16 && wParam != 0) { ShutdownRequested?.Invoke(); return 0; }
            if (message == taskbarCreated) { AddIcon(); if (!Ready) Unavailable?.Invoke(); }
            if (message == Callback)
            {
                switch ((uint)lParam.ToInt64() & 0xffff)
                {
                    case 0x400: case 0x401: Click?.Invoke(); break;
                    case 0x7b: ContextMenu?.Invoke(); break;
                    case 0x203: DoubleClick?.Invoke(); break;
                }
                return 0;
            }
        }
        catch (Exception error) { App.RecordStartupStage("Tray callback: " + error.GetType().Name); }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    public (RectInt32 Anchor, RectInt32 WorkArea, double Scale) Placement()
    {
        var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = window, Id = 1, Guid = guid };
        if (ShellNotifyIconGetRect(ref id, out var rect) != 0)
        { GetCursorPos(out var point); rect = new() { Left = point.X, Top = point.Y, Right = point.X + 1, Bottom = point.Y + 1 }; }
        var monitor = MonitorFromRect(ref rect, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Monitor information is unavailable.");
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96d : 1;
        return (new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
            new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), scale);
    }
    public void Dispose()
    {
        if (window != 0) { var data = Data(); ShellNotifyIcon(2, ref data); DestroyWindow(window); window = 0; }
        if (icon != 0) { DestroyIcon(icon); icon = 0; }
        UnregisterClass(className, GetModuleHandle(null)); Ready = false;
        GC.KeepAlive(procedure);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    { public uint Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct IconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass definition);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string file, uint type, int x, int y, uint flags);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] private static extern bool ShellNotifyIcon(uint operation, ref IconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref IconIdentifier id, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
}
