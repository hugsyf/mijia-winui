namespace MiHome.WinUI.Infrastructure;

internal sealed class ForegroundProgramState
{
    private string current = "";
    private string candidate = "";
    private long candidateSince;

    public void Reset(string path)
    {
        current = candidate = path;
        candidateSince = 0;
    }

    public (string Previous, string Current)? Observe(string path, long now)
    {
        // Windows can briefly report no foreground window while switching desktops or apps.
        if (path.Length == 0) return null;
        if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
        {
            candidate = path;
            candidateSince = now;
        }
        if (string.Equals(current, path, StringComparison.OrdinalIgnoreCase) || now - candidateSince < 600) return null;
        var previous = current;
        current = path;
        return (previous, current);
    }
}
