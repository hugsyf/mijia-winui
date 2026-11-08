namespace MiHome.WinUI.Devices;

internal static class DrawerLayout
{
    public static bool Inline(double width, string mode, bool expanded) => false;
    public static double Width(double width, bool expanded) => Math.Max(0, expanded || width <= 652 ? width : Math.Min(400, width));
}
