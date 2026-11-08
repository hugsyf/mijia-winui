using System.Text.Json;
using MiHome.WinUI.Infrastructure;

internal static class DocumentChecks
{
    public static void Run()
    {
        var count=0;
        void Check(bool condition,string message){count++;if(!condition)throw new Exception(message);}
        const string source="## 标题\r\n\r\n普通 `正文`。\r\n同一段落。\r\n\r\n1. 导出\r\n2. 打包\r\n- 安装\r\n\r\n```json\r\n{\r\n  \"heading\": \"# 不是标题\",\r\n\r\n  \"url\": \"https://example.com\"\r\n}\r\n```";
        var blocks=DocumentParser.Parse(source);
        Check(blocks.Count==6 && blocks[0] is{Kind:"heading",Level:2,Text:"标题"},"Headings, prose, list items and code remain distinct");
        Check(blocks[1].Text=="普通 `正文`。 同一段落。","Markdown soft line breaks form a paragraph without losing inline code");
        Check(blocks.Where(block=>block.Kind=="item").Select(block=>block.Level).SequenceEqual(new[]{1,2,0}),"Ordered list numbers are preserved");
        var code=blocks.Single(block=>block.Kind=="code");
        Check(code.Language=="json" && code.Text.Contains("\n  \"heading\"") && code.Text.Contains("\n\n  \"url\""),"Code indentation and blank lines are preserved for copying");
        Check(JsonDocument.Parse(code.Text).RootElement.GetProperty("heading").GetString()=="# 不是标题","Markdown-like text inside code is not parsed as layout");
        var raw=DocumentParser.Parse("<script>alert('x')</script>\n![image](https://example.com/image.png)");
        Check(raw.Single() is{Kind:"paragraph"} && raw[0].Text.Contains("<script>"),"HTML and remote asset syntax stay literal text");
        Check(DocumentParser.Parse("```powershell\n  do-not-execute\n").Single().Text=="  do-not-execute\n","Unclosed code fences still preserve their content");
        Check(DocumentParser.Parse("~~~json\n{}\n~~~").Single() is{Kind:"code",Text:"{}"},"Tilde fences are supported");
        var guide=DocumentParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"adapter-agent-guide.md")));
        Check(guide.Any(block=>block.Kind=="heading" && block.Text=="验证和交付") && guide.Any(block=>block.Kind=="code" && block.Language=="json" && block.Text.Contains("sdkVersion")),"The actual SDK guide contains formatted sections and a usable JSON example");
        Console.WriteLine($"Passed {count} targeted document checks; no UI or code execution.");
    }
}
