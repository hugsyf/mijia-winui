using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Core;
using MiHome.WinUI.Controls;
namespace MiHome.WinUI.Panels;

public sealed partial class GenericPanel : UserControl, IRefreshablePanel
{
    private readonly PanelContext context = null!;
    private readonly List<PropertyRow> rows = [];
    private readonly HashSet<PropertyRow> advancedRows = [];
    private readonly SemaphoreSlim operations = new(1);
    private JsonElement? lastValues;
    public event Action<DeviceCardState>? CardStateRead;
    private bool refreshing;
    public GenericPanel() => InitializeComponent();
    public GenericPanel(PanelContext context) : this()
    {
        this.context = context;
        Empty.Visibility = context.Schema.Properties.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var primary = context.Schema.Properties.Where(PropertyPresentation.IsPrimary).ToList();
        AddGroups(primary, PrimaryRows, false);
        var additional = context.Schema.Properties.Except(primary).ToList();
        if (additional.Count > 0)
        {
            moreSettings.Visibility = Visibility.Visible;
            var created = false;
            moreSettings.Expanding += (_, _) => { if (!created) { AddGroups(additional, AdditionalRows, true); created = true; } };
        }
    }
    public void SetExpanded(bool expanded)
    {
        moreSettings.Visibility = expanded && context.Schema.Properties.Any(p => !PropertyPresentation.IsPrimary(p)) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AddGroups(List<PropertySchema> properties, StackPanel target, bool advanced)
    {
        var groups = properties.GroupBy(p => (p.Siid, p.Service)).ToList();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (groups.Count > 1)
            {
                var title = PropertyPresentation.Service(group.First());
                if (groups.Count(g => PropertyPresentation.Service(g.First()) == title) > 1) title += $" {index + 1}";
                target.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
            }
            foreach (var prop in group)
            {
                var row = new PropertyRow(prop, WriteAsync);
                rows.Add(row);
                if (advanced) advancedRows.Add(row);
                target.Children.Add(row);
                if (lastValues is { } values) ApplyValue(row, values);
            }
        }
    }
    public async Task RefreshAsync()
    {
        if (refreshing || !await operations.WaitAsync(0)) return;
        refreshing = true;
        try { await ReadCoreAsync(); }
        catch (BridgeException e) when (e.Code == "auth") { throw; }
        catch (Exception e) { lastValues = null; SetStatus(e.Message); foreach (var row in rows) row.MarkStale();
            CardStateRead?.Invoke(new(context.Device.Id, context.Schema, JsonSerializer.SerializeToElement(Array.Empty<object>()), null, "状态暂不可用")); }
        finally { refreshing = false; operations.Release(); }
    }
    private async Task ReadCoreAsync()
    {
        var values = await context.Service.ReadAsync(context.Device.Id);
        ApplySnapshot(values);
        CardStateRead?.Invoke(new(context.Device.Id, context.Schema, values.Clone(), DateTimeOffset.UtcNow, null));
    }
    public void ApplySnapshot(JsonElement values)
    {
        lastValues = values.Clone();
        var failed = 0;
        foreach (var row in rows)
        {
            if (!row.Schema.Readable) continue;
            if (!ApplyValue(row, values) && (!advancedRows.Contains(row) || moreSettings?.IsExpanded == true)) failed++;
        }
        SetStatus(failed == 0 ? null : "部分状态暂时无法更新，请稍后刷新。");
    }
    private void SetStatus(string? message)
    {
        status.Text = message ?? "";
        status.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
    private static bool ApplyValue(PropertyRow row, JsonElement values)
    {
        if (!row.Schema.Readable) return true;
        var found = values.EnumerateArray().FirstOrDefault(v => v.GetProperty("siid").GetInt32() == row.Schema.Siid && v.GetProperty("piid").GetInt32() == row.Schema.Piid);
        if (found.ValueKind == JsonValueKind.Undefined || !found.TryGetProperty("code", out var code) || code.GetInt32() != 0 || !found.TryGetProperty("value", out var value))
        { row.MarkStale("暂不可用"); return false; }
        row.Update(value.Clone());
        return true;
    }
    private async Task WriteAsync(PropertyRow row, object? value)
    {
        if (!await operations.WaitAsync(0)) { row.ShowError("正在读取或写入，请稍后重试。"); return; }
        row.SetWriting(true);
        row.BeginWrite(value);
        try
        {
            await context.Service.WriteAsync(new(context.Device.Id, row.Schema.Siid, row.Schema.Piid), value);
            if (!row.Schema.Readable) { row.AcceptWriteOnly(); return; }
            row.ShowError("等待设备确认…");
            await Task.Delay(800);
            await ReadCoreAsync();
            if (row.HasPendingWrite) row.ShowError("请求已接受，结果待确认。请刷新状态。");
        }
        catch (BridgeException e) when (e.Code is "invalid" or "auth") { row.RejectWrite(e.Message); }
        catch (Exception e) { row.ShowError(e.Message + " 结果待确认，请先刷新状态。"); row.MarkStale(); }
        finally { row.SetWriting(false); operations.Release(); }
    }
}
