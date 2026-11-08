namespace MiHome.WinUI.Devices;

internal static class DeviceCardSizing
{
    public const double RowHeight = 88;
    public static int Rows(string size) => size == "compact" ? 1 : 2;
    public static int Columns(string size) => size == "expanded" ? 2 : 1;
    public static string FromDrag(string start, double columnWidth, double dx, double dy, int columns)
    {
        var width = Columns(start) * columnWidth - 12 + dx;
        var height = Rows(start) * RowHeight - 12 + dy;
        return columns > 1 && width >= columnWidth * 1.5 ? "expanded" : height >= 120 ? "standard" : "compact";
    }
}
