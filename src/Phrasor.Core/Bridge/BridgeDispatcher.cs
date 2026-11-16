using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phrasor.Core.History;
using Phrasor.Core.Home;
using Phrasor.Core.Optimization;
using Phrasor.Core.Theme;
using Phrasor.Core.Web;

namespace Phrasor.Core.Bridge;

/// <summary>
/// Turns one message from the page into one reply. Pure: the caller supplies the sender's URL and frame, the app's
/// <see cref="IMainWindowApi"/>, a log sink and a clock. Every failure becomes an error reply; nothing here throws for bad input.
/// Order of checks: sender (dropped silently), size, rate, JSON shape, command, payload. Not thread-safe: call it from the UI thread.
/// </summary>
public sealed class BridgeDispatcher
{
    public const int MaxMessageBytes = 4 * 1024; // raise per command when a command needs text

    /// <summary>Only <c>tryit.run</c> may be bigger: it carries a draft. 20,000 characters can take 6 bytes each once escaped.</summary>
    public const int MaxTryItBytes = 128 * 1024;
    public const int MaxTryItChars = 20_000;
    private const string TryItCommand = "tryit.run";

    /// <summary>Settings carry the About-me text, typed keys and nine requirement texts of 2,000 characters: 6 bytes each once escaped, about 135 KB at worst.</summary>
    public const int MaxSettingsBytes = 192 * 1024;
    private const string SaveCommand = "settings.save";
    private const string TestCommand = "settings.testConnection";
    public const int MaxIdLength = 64;
    public const int MaxMessagesPerWindow = 30;
    public const int WindowMs = 1000;
    private const int MaxJsonDepth = 8;
    private const int MaxArgLength = 32;

    public static readonly IReadOnlySet<string> CommandNames = new HashSet<string>
    {
        "state.get", "theme.set", "window.minimize", "window.maximize", "window.close", "link.open",
        "home.get", "pause.set", TryItCommand, "tryit.cancel", "clipboard.copy",
        "settings.get", SaveCommand, TestCommand, "shortcut.recordStart", "shortcut.recordStop",
        "data.openFolder", "data.deleteAll",
        "history.list", "history.get", "history.save", "history.copy", "history.delete", "history.deleteAll", "history.setRetention",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IMainWindowApi _api;
    private readonly Action<string> _log;
    private readonly Func<long> _nowMs;
    private readonly Queue<long> _recent = new();

    private bool _limitLogged;
    private string? _command; // set once the command is known, only so the log line can name it

    public BridgeDispatcher(IMainWindowApi api, Action<string> log, Func<long>? nowMs = null)
    {
        _api = api;
        _log = log;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Messages refused because of who sent them.</summary>
    public int DroppedCount { get; private set; }

    /// <summary>The reply as JSON, or null when the message was dropped (wrong origin or not the top frame).</summary>
    public string? Handle(string? sourceUrl, bool isTopFrame, string? message)
    {
        var start = _nowMs();
        var bytes = message is null ? 0 : Encoding.UTF8.GetByteCount(message);
        _command = null;
        if (!isTopFrame || !WebSecurityPolicy.IsAllowedUrl(sourceUrl))
        {
            DroppedCount++;
            _log(BridgeLog.Describe(null, bytes, 0, BridgeError.Dropped));
            return null;
        }

        try
        {
            return Process(message, bytes, start);
        }
        catch (Exception ex)
        {
            _log($"{BridgeLog.Describe(_command, bytes, _nowMs() - start, BridgeError.Failed)} type={ex.GetType().Name}");
            return Error(null, BridgeError.Failed);
        }
    }

    private string Process(string? message, int bytes, long start)
    {
        string Fail(string? id, string kind)
        {
            // A flood must not flood the log: one line per rate-limited window.
            if (kind == BridgeError.RateLimited && _limitLogged) return Error(id, kind);
            _limitLogged |= kind == BridgeError.RateLimited;
            _log(BridgeLog.Describe(_command, bytes, _nowMs() - start, kind));
            return Error(id, kind);
        }

        if (message is null) return Fail(null, BridgeError.BadJson);
        // A message may only be bigger than the default if it names a command that needs room; the real check is repeated once the command is parsed.
        var limit = CommandNames.Select(c => LimitFor(c) > MaxMessageBytes && message.Contains($"\"{c}\"", StringComparison.Ordinal) ? LimitFor(c) : MaxMessageBytes).Max();
        if (bytes > limit) return Fail(null, BridgeError.TooLarge);
        if (!TryAcquire(start)) return Fail(null, BridgeError.RateLimited);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = MaxJsonDepth }); }
        catch (JsonException) { return Fail(null, BridgeError.BadJson); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Fail(null, BridgeError.BadRequest);

            string? id = null, type = null;
            JsonElement? payload = null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "id" when p.Value.ValueKind == JsonValueKind.String: id = p.Value.GetString(); break;
                    case "type" when p.Value.ValueKind == JsonValueKind.String: type = p.Value.GetString(); break;
                    case "payload": payload = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.Clone(); break;
                    default: return Fail(null, BridgeError.BadRequest); // unknown or mistyped top-level field
                }
            }
            if (id is not { Length: > 0 and <= MaxIdLength } || !id.All(IsIdChar) || type is null)
                return Fail(null, BridgeError.BadRequest);
            if (!CommandNames.Contains(type)) return Fail(id, BridgeError.UnknownCommand);

            _command = type;
            if (bytes > LimitFor(type)) return Fail(id, BridgeError.TooLarge);
            object result;
            try { result = Run(type, payload); }
            catch (BadPayloadException) { return Fail(id, BridgeError.BadRequest); }
            catch (TooLargeException) { return Fail(id, BridgeError.TooLarge); }
            catch (BusyException) { return Fail(id, BridgeError.Busy); }
            catch (HistoryUnavailableException) { return Fail(id, BridgeError.HistoryUnavailable); }
            catch (NothingToSaveException) { return Fail(id, BridgeError.NothingToSave); }
            _log(BridgeLog.Describe(type, bytes, _nowMs() - start, BridgeLog.Ok));
            return JsonSerializer.Serialize(new Reply(id, true, result, null), Json);
        }
    }

    private static int LimitFor(string type) => type switch
    {
        TryItCommand => MaxTryItBytes,
        SaveCommand or TestCommand => MaxSettingsBytes,
        _ => MaxMessageBytes,
    };

    private object Run(string type, JsonElement? payload)
    {
        switch (type)
        {
            case "state.get":
                NoPayload(payload);
                var s = _api.GetState();
                return new { theme = ThemeName(s.Theme), dark = s.Dark, maximised = s.Maximised };
            case "theme.set":
                var mode = OneString(payload, "mode") switch
                {
                    "system" => ThemeMode.System,
                    "light" => ThemeMode.Light,
                    "dark" => ThemeMode.Dark,
                    _ => throw new BadPayloadException(),
                };
                _api.SetTheme(mode);
                return new { theme = ThemeName(mode) };
            case "window.minimize": NoPayload(payload); _api.Window(WindowAction.Minimize); return new { };
            case "window.maximize": NoPayload(payload); _api.Window(WindowAction.ToggleMaximize); return new { };
            case "window.close": NoPayload(payload); _api.Window(WindowAction.Close); return new { };
            case "link.open":
                if (!LinkAllowList.TryGet(OneString(payload, "id"), out var url)) throw new BadPayloadException();
                _api.OpenLink(url);
                return new { };
            case "home.get":
                NoPayload(payload);
                return HomeView(_api.GetHome());
            case "pause.set":
                return new { paused = _api.SetPaused(OneBool(payload, "paused")) };
            case TryItCommand:
                var run = _api.StartTryIt(TryItArgs(payload)) ?? throw new BusyException();
                return new { run };
            case "tryit.cancel": NoPayload(payload); _api.CancelTryIt(); return new { };
            case "clipboard.copy": NoPayload(payload); return new { copied = _api.CopyRewrite() };
            case "settings.get": NoPayload(payload); return _api.GetSettings();
            case SaveCommand:
                var saved = _api.SaveSettings(SettingsArgs(payload));
                return new { saved = saved.Saved, settings = saved.Settings, problems = saved.Problems, warnings = saved.Warnings };
            case "shortcut.recordStart": NoPayload(payload); _api.SetShortcutRecording(true); return new { };
            case "shortcut.recordStop": NoPayload(payload); _api.SetShortcutRecording(false); return new { };
            case "data.openFolder":
                _api.OpenDataFolder(OneString(payload, "which") switch
                {
                    "data" => DataFolder.Data,
                    "logs" => DataFolder.Logs,
                    _ => throw new BadPayloadException(),
                });
                return new { };
            case "data.deleteAll":
                if (!OneBool(payload, "confirm")) throw new BadPayloadException(); // the page asks the user first, then sends true
                return new { deleted = _api.DeleteAllData() };
            case "history.list":
                var list = Exact(payload, "query", "limit");
                if (list[0].ValueKind != JsonValueKind.String || list[1].ValueKind != JsonValueKind.Number) throw new BadPayloadException();
                var query = list[0].GetString()!.Trim();
                if (query.Length > HistoryView.MaxQueryChars || !list[1].TryGetInt32(out var limit) || limit is < 0 or > HistoryStore.MaxEntries) throw new BadPayloadException();
                return _api.ListHistory(query, limit);
            case "history.get": return (object?)_api.GetHistory(HistoryId(payload)) ?? throw new BadPayloadException();
            case "history.save":
                NoPayload(payload);
                var result = _api.SaveHistory();
                return result == HistorySaveResult.NothingToSave ? throw new NothingToSaveException() : new { already = result == HistorySaveResult.AlreadySaved };
            case "history.copy":
                var copy = Exact(payload, "id", "part");
                if (copy[0].ValueKind != JsonValueKind.String || copy[1].ValueKind != JsonValueKind.String) throw new BadPayloadException();
                var part = copy[1].GetString();
                if (part is not ("original" or "rewrite")) throw new BadPayloadException();
                return new { copied = _api.CopyHistory(IdOrThrow(copy[0].GetString()), part == "original") };
            case "history.delete": return new { deleted = _api.DeleteHistory(HistoryId(payload)) };
            case "history.deleteAll":
                if (!OneBool(payload, "confirm")) throw new BadPayloadException();
                _api.DeleteAllHistory();
                return new { };
            case "history.setRetention":
                var days = Exact(payload, "days")[0];
                if (days.ValueKind != JsonValueKind.Number || !days.TryGetInt32(out var d) || !HistoryRetention.Allowed.Contains(d)) throw new BadPayloadException();
                _api.SetHistoryRetention(d);
                return new { days = d };
            case TestCommand:
                var test = _api.StartConnectionTest(SettingsArgs(payload)) ?? throw new BusyException();
                return new { run = test };
            default: throw new BadPayloadException();
        }
    }

    private static string HistoryId(JsonElement? p) => OneString(p, "id");

    // A saved item's id is 32 lowercase hex characters; anything else cannot match, so it is refused here.
    private static string IdOrThrow(string? id) =>
        id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? id : throw new BadPayloadException();

    // The payload must be exactly an object with these properties (any order); returns their values in the order of the names.
    private static JsonElement[] Exact(JsonElement? p, params string[] names)
    {
        if (p is not { ValueKind: JsonValueKind.Object } e || e.EnumerateObject().Count() != names.Length) throw new BadPayloadException();
        return names.Select(n => e.TryGetProperty(n, out var v) ? v : throw new BadPayloadException()).ToArray();
    }

    private static SettingsDraft SettingsArgs(JsonElement? payload) => SettingsPayload.TryParse(payload, out var draft) switch
    {
        SettingsPayload.Outcome.Ok => draft,
        SettingsPayload.Outcome.TooLarge => throw new TooLargeException(),
        _ => throw new BadPayloadException(),
    };

    private static object HomeView(HomeStatus h) => new
    {
        state = h.State.ToString().ToLowerInvariant(),
        paused = h.Paused,
        provider = h.Provider,
        model = h.Model,
        keyRequired = h.KeyRequired,
        keySet = h.KeySet,
        defaultMode = h.DefaultMode.ToString(),
        shortcuts = h.Shortcuts.Select(x => new { id = x.Id, label = x.Label, chord = x.Chord, registered = x.Registered }),
    };

    // Exactly { text, task, mode }: a bounded, non-blank draft and two names from fixed lists (enum names, not numbers).
    private static TryItRequest TryItArgs(JsonElement? p)
    {
        if (p is not { ValueKind: JsonValueKind.Object } e) throw new BadPayloadException();
        string? text = null, task = null, mode = null;
        var count = 0;
        foreach (var prop in e.EnumerateObject())
        {
            count++;
            if (prop.Value.ValueKind != JsonValueKind.String) throw new BadPayloadException();
            switch (prop.Name)
            {
                case "text": text = prop.Value.GetString(); break;
                case "task": task = prop.Value.GetString(); break;
                case "mode": mode = prop.Value.GetString(); break;
            }
        }
        if (count != 3 || text is null || task is null || mode is null || string.IsNullOrWhiteSpace(text)) throw new BadPayloadException();
        if (text.Length > MaxTryItChars) throw new TooLargeException();
        if (!Enum.GetNames<TaskType>().Contains(task) || !Enum.GetNames<OptimizeMode>().Contains(mode)) throw new BadPayloadException();
        return new TryItRequest(text, Enum.Parse<TaskType>(task), Enum.Parse<OptimizeMode>(mode));
    }

    private static string ThemeName(ThemeMode m) => m.ToString().ToLowerInvariant();

    private static bool IsIdChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';

    private static void NoPayload(JsonElement? p)
    {
        if (p is { } e && !(e.ValueKind == JsonValueKind.Object && !e.EnumerateObject().Any())) throw new BadPayloadException();
    }

    // The payload must be exactly { "<name>": "<short string>" }.
    private static string OneString(JsonElement? p, string name)
    {
        if (p is not { ValueKind: JsonValueKind.Object } e) throw new BadPayloadException();
        string? value = null;
        var count = 0;
        foreach (var prop in e.EnumerateObject())
        {
            count++;
            if (prop.Name == name && prop.Value.ValueKind == JsonValueKind.String) value = prop.Value.GetString();
        }
        if (count != 1 || value is not { Length: > 0 and <= MaxArgLength }) throw new BadPayloadException();
        return value;
    }

    private static bool OneBool(JsonElement? p, string name)
    {
        if (p is not { ValueKind: JsonValueKind.Object } e) throw new BadPayloadException();
        var props = e.EnumerateObject().ToList();
        if (props.Count != 1 || props[0].Name != name || props[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new BadPayloadException();
        return props[0].Value.GetBoolean();
    }

    private bool TryAcquire(long now)
    {
        while (_recent.Count > 0 && now - _recent.Peek() >= WindowMs) _recent.Dequeue();
        if (_recent.Count >= MaxMessagesPerWindow) return false;
        _limitLogged = false;
        _recent.Enqueue(now);
        return true;
    }

    private static string Error(string? id, string kind) => JsonSerializer.Serialize(new Reply(id, false, null, kind), Json);

    private sealed record Reply(string? Id, bool Ok, object? Payload, string? Error);

    private sealed class BadPayloadException : Exception;
    private sealed class TooLargeException : Exception;
    private sealed class BusyException : Exception;
    private sealed class NothingToSaveException : Exception;
}
