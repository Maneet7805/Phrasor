using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const int MaxIdLength = 64;
    public const int MaxMessagesPerWindow = 30;
    public const int WindowMs = 1000;
    private const int MaxJsonDepth = 8;
    private const int MaxArgLength = 32;

    public static readonly IReadOnlySet<string> CommandNames = new HashSet<string>
    {
        "state.get", "theme.set", "window.minimize", "window.maximize", "window.close", "link.open",
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
        if (bytes > MaxMessageBytes) return Fail(null, BridgeError.TooLarge);
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
            object result;
            try { result = Run(type, payload); }
            catch (BadPayloadException) { return Fail(id, BridgeError.BadRequest); }
            _log(BridgeLog.Describe(type, bytes, _nowMs() - start, BridgeLog.Ok));
            return JsonSerializer.Serialize(new Reply(id, true, result, null), Json);
        }
    }

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
            default: throw new BadPayloadException();
        }
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
}
