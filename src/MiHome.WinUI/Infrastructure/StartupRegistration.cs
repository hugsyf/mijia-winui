using Microsoft.Win32;

namespace MiHome.WinUI.Infrastructure;

internal static class StartupRegistration
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MiHomeWinUI";
    public static bool Registered
    {
        get { try { using var key = Registry.CurrentUser.OpenSubKey(Run); return key?.GetValue(Name) is string; } catch { return false; } }
    }
    public static bool DisabledByWindows
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                return Registered && key?.GetValue(Name) is byte[] { Length: > 0 } state && state[0] is 3 or 7;
            }
            catch { return false; }
        }
    }
    public static bool Enabled => Registered && !DisabledByWindows;
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Run);
        if (!enabled) { key.DeleteValue(Name, false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        key.SetValue(Name, $"\"{executable}\" --startup", RegistryValueKind.String);
    }
}
