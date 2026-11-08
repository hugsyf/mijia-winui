using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MiHome.WinUI.Infrastructure;
using Windows.ApplicationModel.DataTransfer;

namespace MiHome.WinUI.Controls;

internal static class DocumentPresenter
{
    public static FrameworkElement Build(string markdown, bool skipTitle = false, bool compact = false)
    {
        var document = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        var blocks = DocumentParser.Parse(markdown);
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (skipTitle && i == 0 && block is { Kind: "heading", Level: 1 }) continue;
            if (block.Kind == "code")
            {
                var view = new CodeBlockView(); view.SetCode(block.Language,block.Text);
                document.Children.Add(view); continue;
            }
            var text = Plain("");
            foreach (var fragment in Regex.Split(block.Text, @"(`[^`\n]+`|\*\*[^*\n]+\*\*)"))
            {
                if (fragment.StartsWith('`') && fragment.EndsWith('`') && fragment.Length > 1)
                    text.Inlines.Add(new Run { Text = fragment[1..^1], FontFamily = new FontFamily("Consolas"), FontSize = 13 });
                else if (fragment.StartsWith("**") && fragment.EndsWith("**") && fragment.Length > 3)
                    text.Inlines.Add(new Run { Text = fragment[2..^2], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                else text.Inlines.Add(new Run { Text = fragment });
            }
            if (block.Kind == "heading")
            {
                text.FontSize = block.Level == 1 ? compact ? 20 : 28 : block.Level == 2 ? 18 : 16;
                text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; text.LineHeight = 0;
                text.Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0);
            }
            if (block.Kind == "item")
            {
                var row = new Grid { ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
                row.Children.Add(Plain(block.Level>0?block.Level+".":"•")); Grid.SetColumn(text, 1); row.Children.Add(text); document.Children.Add(row);
            }
            else document.Children.Add(text);
        }
        return document;
    }
    public static TextBlock Plain(string text) => new()
    {
        Text = text, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.Normal, LineHeight = 22, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
    };
    public static void Copy(string text)
    { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); }
}
