using System.Text.Json;
using System.Text.Json.Serialization;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;

namespace Phrasor.Core.Bridge;

/// <summary>A draft typed on the page. The page picks the task and mode; everything else comes from the app's settings.</summary>
public sealed record TryItRequest(string Text, TaskType Task, OptimizeMode Mode);

/// <summary>What the page may see of a finished run: only text the optimiser produced.</summary>
public sealed record TryItOutput(string Rewrite, string Verdict, string Task, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings);

/// <summary>
/// Plain failure kinds for Try-it, each with one fixed message. The message is looked up by kind, so provider text,
/// exception text and the draft can never reach the page (or the log) through here.
/// </summary>
public static class TryItErrors
{
    public const string Cancelled = "cancelled", NoKey = "no_key", NoModel = "no_model", Empty = "empty", TooLong = "too_long",
        KeyRejected = "key_rejected", RateLimit = "rate_limit", Offline = "offline", Timeout = "timeout",
        ProviderError = "provider_error", BadResponse = "bad_response", Failed = "failed";

    private static readonly Dictionary<string, string> Messages = new()
    {
        [Cancelled] = "Cancelled.",
        [NoKey] = "Add your API key in Settings, then try again.",
        [NoModel] = "No model is set up. Check Settings, then try again.",
        [Empty] = "Type or paste a draft first.",
        [TooLong] = "That draft is too long to rewrite here. Try a shorter one.",
        [KeyRejected] = "The provider did not accept your API key. Check it in Settings.",
        [RateLimit] = "The provider's rate limit was reached. Wait a minute and try again.",
        [Offline] = "Couldn't reach the provider. Check your internet connection.",
        [Timeout] = "The provider took too long to answer. Try again.",
        [ProviderError] = "The provider couldn't complete this request. Try again shortly.",
        [BadResponse] = "The provider's reply couldn't be understood. Try again.",
        [Failed] = "Something went wrong. Try again.",
    };

    public static IReadOnlyCollection<string> Kinds => Messages.Keys;

    public static string MessageFor(string kind) => Messages.GetValueOrDefault(kind, Messages[Failed]);

    /// <summary>Maps the optimiser's existing failures onto a kind. Reads types and enums only, never a message.</summary>
    public static string KindOf(Exception ex) => ex switch
    {
        OperationCanceledException => Cancelled,
        MissingKeyException => NoKey,
        LlmException llm => FromLlm(llm.Kind),
        OptimizeException { Failure: OptimizeFailure.Empty } => Empty,
        OptimizeException { Failure: OptimizeFailure.TooLong } => TooLong,
        OptimizeException { Failure: OptimizeFailure.NoEndpoint } => NoModel,
        OptimizeException { Failure: OptimizeFailure.BadResponse } => BadResponse,
        OptimizeException { InnerException: { } inner } => KindOf(inner),
        _ => Failed,
    };

    private static string FromLlm(LlmErrorKind kind) => kind switch
    {
        LlmErrorKind.Auth => KeyRejected,
        LlmErrorKind.RateLimit => RateLimit,
        LlmErrorKind.Network => Offline,
        LlmErrorKind.Timeout => Timeout,
        LlmErrorKind.Cancelled => Cancelled,
        LlmErrorKind.InvalidJsonOutput or LlmErrorKind.Truncated => BadResponse,
        _ => ProviderError,
    };
}

/// <summary>Messages the app pushes to the page without being asked. Shaped like replies, but carry an event name instead of an id.</summary>
public static class BridgeEvents
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string TryItDone(int run, TryItOutput output) =>
        JsonSerializer.Serialize(new { @event = "tryit", run, ok = true, payload = output }, Json);

    public static string TryItFailed(int run, string kind) =>
        JsonSerializer.Serialize(new { @event = "tryit", run, ok = false, error = kind, message = TryItErrors.MessageFor(kind) }, Json);
}
