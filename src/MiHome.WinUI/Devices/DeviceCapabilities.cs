using System.Text.Json;
using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

internal sealed record CapabilityProperty(string Service, string Property, string[] Types, string[] Units);
internal sealed record CapabilityTemplate(string Category, string[] DeviceTypes, CapabilityProperty[] Required, CapabilityProperty[] Optional);
internal sealed record CapabilityMatch(string Category, int Siid, List<PropertySchema> Properties);

internal static class DeviceCapabilities
{
    private static readonly CapabilityTemplate[] templates = Load();
    private static CapabilityTemplate[] Load()
    {
        using var stream = typeof(DeviceCapabilities).Assembly.GetManifestResourceStream("MiHome.DeviceTemplates.json")!;
        return JsonSerializer.Deserialize<CapabilityTemplate[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private static bool IsUrn(string urn, string kind, string name) => urn.StartsWith($"urn:miot-spec-v2:{kind}:{name}:", StringComparison.Ordinal);
    private static bool Matches(PropertySchema p, CapabilityProperty rule) => p.Readable &&
        IsUrn(p.ServiceType, "service", rule.Service) && IsUrn(p.PropertyType, "property", rule.Property) &&
        rule.Types.Contains(p.Type) && (rule.Units.Length == 0 || rule.Units.Contains(p.Unit));
    internal static string? CandidateCategory(DeviceIdentity device) => templates.FirstOrDefault(t =>
        t.DeviceTypes.Any(type => IsUrn(device.SpecType, "device", type)))?.Category;
    internal static CapabilityMatch? Match(DeviceIdentity device, DeviceSchema? schema)
    {
        if (schema is null || DeviceModels.Find(device) is not null) return null;
        var template = templates.FirstOrDefault(t => t.DeviceTypes.Any(type => IsUrn(device.SpecType, "device", type)));
        if (template is null) return null;
        // Required readings must belong to one unambiguous service instance.
        var groups = schema.Properties.GroupBy(p => p.Siid).Where(group =>
            template.Required.All(rule => group.Count(p => Matches(p, rule)) == 1)).ToList();
        if (groups.Count != 1) return null;
        var siid = groups[0].Key;
        var selected = new List<PropertySchema>();
        foreach (var rule in template.Required.Concat(template.Optional))
        {
            var matches = schema.Properties.Where(p => Matches(p, rule) &&
                (rule.Service is "battery" or "single-use-battery" or "power-consumption" || p.Siid == siid)).ToList();
            if (matches.Count == 1) selected.Add(matches[0]);
        }
        // Preserve guard metadata even if it is unreadable; it must not disappear from cards.
        selected.AddRange(schema.Properties.Where(p => p.Siid == siid &&
            (IsUrn(p.PropertyType, "property", "fault") ||
             template.Category == "outlet" && IsUrn(p.PropertyType, "property", "mode"))));
        return new(template.Category, siid, selected.DistinctBy(p => (p.Siid, p.Piid)).ToList());
    }
}
