using System.Text.Json;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Bridge;

/// <summary>
/// Strict reader for the Settings payload. Every object has exactly the listed fields, strings are length-capped and
/// checked against fixed lists where a list exists, and anything else is refused. Never echoes a value back.
/// </summary>
public static class SettingsPayload
{
    public const int MaxPersonalContextChars = 4_000;
    public const int MaxModelChars = 100;
    public const int MaxUrlChars = 200;
    public const int MaxKeyChars = 500;

    public enum Outcome { Ok, Bad, TooLarge }

    public static Outcome TryParse(JsonElement? payload, out SettingsDraft draft)
    {
        draft = null!;
        try
        {
            if (payload is not { ValueKind: JsonValueKind.Object } root) return Outcome.Bad;
            var top = Exact(root, "general", "model", "keys");
            var g = Exact(top["general"], "theme", "defaultMode", "reviewBeforeReplace", "startWithWindows", "sendAppHint",
                "repairDroppedDetails", "checkForUpdates", "personalContext");
            var m = Exact(top["model"], "primary", "fallback");
            var k = Exact(top["keys"], "set", "remove");

            var context = Str(g["personalContext"], 0, int.MaxValue).Trim();
            if (context.Length > MaxPersonalContextChars) return Outcome.TooLarge;

            var typed = new Dictionary<string, string>();
            if (k["set"].ValueKind != JsonValueKind.Object) return Outcome.Bad;
            foreach (var p in k["set"].EnumerateObject())
            {
                if (!SettingsPage.KeyProviders.Contains(p.Name)) return Outcome.Bad;
                var key = Str(p.Value, 1, MaxKeyChars).Trim();
                if (key.Length == 0 || key.Any(char.IsControl)) return Outcome.Bad;
                typed[p.Name] = key;
            }
            if (k["remove"].ValueKind != JsonValueKind.Array) return Outcome.Bad;
            var remove = new HashSet<string>();
            foreach (var item in k["remove"].EnumerateArray())
            {
                var id = Str(item, 1, 16);
                if (!SettingsPage.KeyProviders.Contains(id) || typed.ContainsKey(id)) return Outcome.Bad;
                remove.Add(id);
            }

            draft = new SettingsDraft(
                Theme: Str(g["theme"], 1, 16) switch
                {
                    "system" => ThemeMode.System,
                    "light" => ThemeMode.Light,
                    "dark" => ThemeMode.Dark,
                    _ => throw new FormatException(),
                },
                DefaultMode: Enum.Parse<OptimizeMode>(OneOf(Str(g["defaultMode"], 1, 16), Enum.GetNames<OptimizeMode>())),
                ReviewBeforeReplace: Bool(g["reviewBeforeReplace"]), StartWithWindows: Bool(g["startWithWindows"]),
                SendAppHint: Bool(g["sendAppHint"]), RepairDroppedDetails: Bool(g["repairDroppedDetails"]),
                CheckForUpdates: Bool(g["checkForUpdates"]), PersonalContext: context,
                Primary: Endpoint(m["primary"], primary: true), Fallback: Endpoint(m["fallback"], primary: false),
                NewKeys: typed, RemoveKeys: remove);
            return Outcome.Ok;
        }
        catch (FormatException) { return Outcome.Bad; }
    }

    private static EndpointDraft Endpoint(JsonElement e, bool primary)
    {
        var o = primary ? Exact(e, "provider", "model", "baseUrl") : Exact(e, "enabled", "provider", "model", "baseUrl");
        var provider = OneOf(Str(o["provider"], 1, 16), ProviderPresets.All.Select(p => p.Id));
        var model = Str(o["model"], primary ? 1 : 0, MaxModelChars).Trim();
        if ((primary && model.Length == 0) || model.Any(char.IsControl)) throw new FormatException();
        var url = Str(o["baseUrl"], 0, MaxUrlChars).Trim();
        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0))
            throw new FormatException();
        // Only custom and local providers use an address; any other provider keeps its preset's.
        var keepUrl = url.Length > 0 && SettingsPage.IsUrlProvider(provider);
        return new EndpointDraft(provider, model, keepUrl ? url : null, primary || Bool(o["enabled"]));
    }

    private static Dictionary<string, JsonElement> Exact(JsonElement e, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new FormatException();
        var map = new Dictionary<string, JsonElement>();
        foreach (var p in e.EnumerateObject())
        {
            if (!names.Contains(p.Name) || !map.TryAdd(p.Name, p.Value)) throw new FormatException();
        }
        if (map.Count != names.Length) throw new FormatException();
        return map;
    }

    private static string Str(JsonElement e, int min, int max)
    {
        if (e.ValueKind != JsonValueKind.String) throw new FormatException();
        var s = e.GetString()!;
        if (s.Length < min || s.Length > max) throw new FormatException();
        return s;
    }

    private static bool Bool(JsonElement e) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new FormatException();

    private static string OneOf(string value, IEnumerable<string> allowed) =>
        allowed.FirstOrDefault(a => a == value) ?? throw new FormatException();
}
