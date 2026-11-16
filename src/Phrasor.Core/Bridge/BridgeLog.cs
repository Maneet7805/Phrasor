namespace Phrasor.Core.Bridge;

/// <summary>Error kinds in a reply. A kind never carries the message, the payload or an exception text.</summary>
public static class BridgeError
{
    public const string BadJson = "bad_json";
    public const string TooLarge = "too_large";
    public const string RateLimited = "rate_limited";
    public const string UnknownCommand = "unknown_command";
    public const string BadRequest = "bad_request";
    public const string Failed = "failed";
    public const string Busy = "busy";
    public const string Dropped = "dropped";
    public const string HistoryUnavailable = "history_unavailable";
    public const string NothingToSave = "nothing_to_save";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        BadJson, TooLarge, RateLimited, UnknownCommand, BadRequest, Failed, Dropped, Busy, HistoryUnavailable, NothingToSave,
    };
}

/// <summary>
/// The only way the bridge writes a log line. It takes a command name, a size, a time and an outcome: nothing that can
/// carry page text. A command name that is not in the table is written as "-", so a made-up name never reaches the log.
/// </summary>
public static class BridgeLog
{
    public const string Ok = "ok";

    public static string Describe(string? command, int bytes, long milliseconds, string outcome)
    {
        var name = command is not null && BridgeDispatcher.CommandNames.Contains(command) ? command : "-";
        var result = outcome == Ok || BridgeError.All.Contains(outcome) ? outcome : "?";
        return $"bridge cmd={name} bytes={bytes} ms={milliseconds} outcome={result}";
    }
}

public static class TryItLog
{
    /// <summary>"small", "medium" or "large": the only thing the log says about a draft's size.</summary>
    public static string SizeClass(int chars) => chars < 500 ? "small" : chars < 5_000 ? "medium" : "large";

    /// <summary>One line per finished run: an outcome that is "ok" or a known error kind, a size class and a time. No text.</summary>
    public static string Describe(string outcome, int chars, long milliseconds) =>
        $"tryit outcome={(outcome == BridgeLog.Ok || TryItErrors.Kinds.Contains(outcome) ? outcome : "?")} size={SizeClass(chars)} ms={milliseconds}";
}
