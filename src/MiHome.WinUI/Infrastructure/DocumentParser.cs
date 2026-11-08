using System.Text.RegularExpressions;

namespace MiHome.WinUI.Infrastructure;

internal sealed record DocumentBlock(string Kind, string Text, int Level = 0, string Language = "");

// A small display-only Markdown subset. No HTML, remote assets or executable content.
internal static class DocumentParser
{
    public static List<DocumentBlock> Parse(string source)
    {
        var result = new List<DocumentBlock>();
        var paragraph = new List<string>();
        var code = new List<string>();
        string? fence = null; string language = "";
        void Flush()
        {
            if (paragraph.Count == 0) return;
            result.Add(new("paragraph", string.Join(" ", paragraph))); paragraph.Clear();
        }
        foreach (var line in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                { result.Add(new("code", string.Join("\n", code), Language: language)); code.Clear(); fence = null; }
                else code.Add(line);
                continue;
            }
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            { Flush(); fence = trimmed[..3]; language = Regex.Match(trimmed[3..].Trim(), @"^[A-Za-z0-9_+-]{1,24}").Value; continue; }
            if (trimmed.Length == 0) { Flush(); continue; }
            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.+)$");
            if (heading.Success)
            { Flush(); result.Add(new("heading", heading.Groups[2].Value, heading.Groups[1].Length)); continue; }
            var item = Regex.Match(trimmed, @"^(?:[-*+]\s+|(\d+)[.)]\s+)(.+)$");
            if (item.Success)
            { Flush(); result.Add(new("item", item.Groups[2].Value, int.TryParse(item.Groups[1].Value,out var order)?order:0)); continue; }
            paragraph.Add(trimmed);
        }
        if (fence is not null) result.Add(new("code", string.Join("\n", code), Language: language));
        Flush(); return result;
    }
}
