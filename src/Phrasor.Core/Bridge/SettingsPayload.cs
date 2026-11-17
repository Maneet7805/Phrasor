using System.Text.Json;
using System.Text.RegularExpressions;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Bridge;

/// <summary>
/// Strict reader for the Settings payload. Every object has exactly the listed fields, strings are length-capped and
/// checked against fixed lists where a list exists, and anything else is refused. Never echoes a value back.
/// </summary>
public static partial class SettingsPayload
{
    public const int MaxPersonalContextChars = 4_000;
    public const int MaxModelChars = 100;
    public const int MaxUrlChars = 200;
    public const int MaxKeyChars = 500;
    public const int MaxShortcutChars = 40;
    public const int MaxLanguageChars = 12;

    public enum Outcome { Ok, Bad, TooLarge }

    public static Outcome TryParse(JsonElement? payload, out SettingsDraft draft)
    {
        draft = null!;
        try
        {
            if (payload is not { ValueKind: JsonValueKind.Object } root) return Outcome.Bad;
            var top = Exact(root, "general", "model", "review", "keys", "shortcuts", "voice", "requirements");
            var g = Exact(top["general"], "theme", "defaultMode", "reviewBeforeReplace", "startWithWindows", "sendAppHint",
                "repairDroppedDetails", "checkForUpdates", "personalContext");
            var models = Models(top["model"]);
            var review = Review(top["review"]);
            var k = Exact(top["keys"], "set", "remove");
            var sc = Exact(top["shortcuts"], "optimize", "optimizeSelection", "voice");
            var v = Exact(top["voice"], "enabled", "provider", "model", "language", "autoOptimize", "maxSeconds");
            var rq = Exact(top["requirements"], [.. SettingsPage.RequirementIds]);

            var context = Str(g["personalContext"], 0, int.MaxValue).Trim();
            if (context.Length > MaxPersonalContextChars) return Outcome.TooLarge;

            var requirements = new Dictionary<string, string>();
            foreach (var (id, value) in rq)
            {
                var text = Str(value, 0, int.MaxValue).Replace("\r\n", "\n").Trim();
                if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) return Outcome.Bad;
                if (text.Length > RequirementsAppender.MaxLength) return Outcome.TooLarge;
                requirements[id] = text;
            }

            var voice = Voice(v);

            var typed = new Dictionary<string, string>();
            if (k["set"].ValueKind != JsonValueKind.Object) return Outcome.Bad;
            foreach (var p in k["set"].EnumerateObject())
            {
                if (!IsKeyId(p.Name, models)) return Outcome.Bad;
                var key = Str(p.Value, 1, MaxKeyChars).Trim();
                if (key.Length == 0 || key.Any(char.IsControl)) return Outcome.Bad;
                typed[p.Name] = key;
            }
            if (k["remove"].ValueKind != JsonValueKind.Array) return Outcome.Bad;
            var remove = new HashSet<string>();
            foreach (var item in k["remove"].EnumerateArray())
            {
                var id = Str(item, 1, 20);
                if (!IsKeyId(id, models) || typed.ContainsKey(id)) return Outcome.Bad;
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
                Models: models, Review: review,
                Shortcuts: new ShortcutsDraft(Chord(sc["optimize"]), Chord(sc["optimizeSelection"]), Chord(sc["voice"])),
                Voice: voice, Requirements: requirements,
                NewKeys: typed, RemoveKeys: remove);
            return Outcome.Ok;
        }
        catch (FormatException) { return Outcome.Bad; }
    }

    // Text only; whether it is a usable chord is for SettingsPage.CheckShortcuts, which can say why in plain words.
    private static string Chord(JsonElement e)
    {
        var text = Str(e, 0, MaxShortcutChars).Trim();
        if (text.Any(char.IsControl)) throw new FormatException();
        return text;
    }

    private static VoiceDraft Voice(Dictionary<string, JsonElement> v)
    {
        var provider = OneOf(Str(v["provider"], 1, 16), SettingsPage.VoiceProviderIds);
        var model = Str(v["model"], 0, MaxModelChars).Trim();
        var language = Str(v["language"], 0, MaxLanguageChars).Trim();
        var enabled = Bool(v["enabled"]);
        if (model.Any(char.IsControl) || (enabled && model.Length == 0)) throw new FormatException();
        if (language.Length > 0 && !LanguageCode().IsMatch(language)) throw new FormatException();
        if (v["maxSeconds"].ValueKind != JsonValueKind.Number || !v["maxSeconds"].TryGetInt32(out var seconds)
            || seconds is < SettingsPage.VoiceMinSeconds or > SettingsPage.VoiceMaxSeconds) throw new FormatException();
        return new VoiceDraft(enabled, provider, model, language, Bool(v["autoOptimize"]), seconds);
    }

    // "en", "ms", "zh-CN": an ISO 639 code with an optional region or script.
    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    private static partial Regex LanguageCode();

    // A provider key, or the own key of a model that is saved here with its own key.
    private static bool IsKeyId(string id, ModelsDraft models) =>
        SettingsPage.KeyProviders.Contains(id) ||
        (ModelList.IsKeyIdOfModel(id) && models.Saved.Any(m => m.SeparateKey && ModelList.KeyIdFor(m.Id) == id));

    private static ModelsDraft Models(JsonElement e)
    {
        var o = Exact(e, "saved", "default", "backups");
        if (o["saved"].ValueKind != JsonValueKind.Array || o["backups"].ValueKind != JsonValueKind.Array) throw new FormatException();
        var saved = new List<ModelDraft>();
        foreach (var item in o["saved"].EnumerateArray())
        {
            var m = Exact(item, "id", "provider", "model", "baseUrl", "separateKey");
            var id = Str(m["id"], 1, 12);
            if (!ModelList.IsValidId(id) || saved.Any(x => x.Id == id)) throw new FormatException();
            var provider = OneOf(Str(m["provider"], 1, 16), ProviderPresets.All.Select(p => p.Id));
            var model = Str(m["model"], 1, MaxModelChars).Trim();
            if (model.Length == 0 || model.Any(char.IsControl)) throw new FormatException();
            var url = Str(m["baseUrl"], 0, MaxUrlChars).Trim();
            if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0))
                throw new FormatException();
            // Only custom and local providers use an address; any other provider keeps its preset's.
            var keepUrl = url.Length > 0 && SettingsPage.IsUrlProvider(provider);
            saved.Add(new ModelDraft(id, provider, model, keepUrl ? url : null, Bool(m["separateKey"])));
        }
        if (saved.Count is 0 or > ModelList.MaxSavedModels) throw new FormatException();
        var def = Str(o["default"], 1, 12);
        if (saved.All(m => m.Id != def)) throw new FormatException();
        var backups = new List<string>();
        foreach (var item in o["backups"].EnumerateArray())
        {
            var id = Str(item, 1, 12);
            if (id == def || backups.Contains(id) || saved.All(m => m.Id != id)) throw new FormatException();
            backups.Add(id);
        }
        return new ModelsDraft(saved, def, backups);
    }

    private static ReviewDraft Review(JsonElement e)
    {
        var o = Exact(e, "taskOrder", "barCount", "defaultTask", "highlightChanges", "rememberLastChoice", "prefetchOtherModes");
        if (o["taskOrder"].ValueKind != JsonValueKind.Array) throw new FormatException();
        var tasks = Enum.GetNames<TaskType>().Where(n => n != nameof(TaskType.Auto)).ToList();
        var order = new List<string>();
        foreach (var item in o["taskOrder"].EnumerateArray())
        {
            var name = OneOf(Str(item, 1, 16), tasks);
            if (order.Contains(name)) throw new FormatException();
            order.Add(name);
        }
        if (order.Count != tasks.Count) throw new FormatException();
        if (o["barCount"].ValueKind != JsonValueKind.Number || !o["barCount"].TryGetInt32(out var bar)
            || bar is < ReviewWindowSettings.MinBarCount or > ReviewWindowSettings.MaxBarCount) throw new FormatException();
        return new ReviewDraft(order, bar, OneOf(Str(o["defaultTask"], 1, 16), Enum.GetNames<TaskType>()),
            Bool(o["highlightChanges"]), Bool(o["rememberLastChoice"]), Bool(o["prefetchOtherModes"]));
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
