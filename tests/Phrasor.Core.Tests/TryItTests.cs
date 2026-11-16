using System.Text.Json;
using System.Text.Json.Nodes;
using Phrasor.Core.Bridge;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class TryItTests
{
    private const string Origin = WebSecurityPolicy.StartUrl;
    private const string Marker = "SECRETWORD";
    private readonly BridgeDispatcherTests.FakeApi _api = new();
    private readonly List<string> _log = [];
    private long _now = 1_000;

    private BridgeDispatcher Make() => new(_api, _log.Add, () => _now);

    private static string Run(string text, string task = "Auto", string mode = "Standard", string id = "t1") =>
        JsonSerializer.Serialize(new { id, type = "tryit.run", payload = new { text, task, mode } });

    private static string Msg(string type, string? payload = null) =>
        $$"""{"id":"a1","type":"{{type}}"{{(payload is null ? "" : $",\"payload\":{payload}")}}}""";

    private static string ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.GetProperty("error").GetString()!;

    [Fact]
    public void Run_passes_the_draft_task_and_mode_to_the_app_and_returns_the_run_number()
    {
        var reply = Make().Handle(Origin, true, Run("fix my login bug", "Debugging", "Light"));
        Assert.Equal(7, JsonDocument.Parse(reply!).RootElement.GetProperty("payload").GetProperty("run").GetInt32());
        Assert.Equal(new TryItRequest("fix my login bug", TaskType.Debugging, OptimizeMode.Light), _api.Runs.Single());
    }

    [Fact]
    public void A_second_run_while_one_is_going_is_busy_and_starts_nothing()
    {
        _api.NextRun = null;
        Assert.Equal(BridgeError.Busy, ErrorOf(Make().Handle(Origin, true, Run("hello"))));
        Assert.Empty(_api.Runs);
    }

    [Theory]
    [InlineData("""{"text":"hi","task":"Auto"}""")]
    [InlineData("""{"text":"hi","task":"Auto","mode":"Standard","extra":"x"}""")]
    [InlineData("""{"text":"hi","task":"auto","mode":"Standard"}""")]
    [InlineData("""{"text":"hi","task":"1","mode":"Standard"}""")]
    [InlineData("""{"text":"hi","task":"Auto","mode":"Huge"}""")]
    [InlineData("""{"text":"hi","task":"Auto","mode":2}""")]
    [InlineData("""{"text":"   ","task":"Auto","mode":"Standard"}""")]
    [InlineData("""{"text":5,"task":"Auto","mode":"Standard"}""")]
    [InlineData("""[]""")]
    public void A_bad_payload_is_refused_and_starts_nothing(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("tryit.run", payload))));
        Assert.Empty(_api.Runs);
    }

    [Fact]
    public void Run_without_a_payload_is_refused() =>
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("tryit.run"))));

    [Fact]
    public void A_draft_at_the_limit_is_accepted_even_when_every_character_needs_a_six_byte_escape()
    {
        var message = Run(new string('\u0001', BridgeDispatcher.MaxTryItChars)); // each becomes \u0001 in the JSON
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(message) <= BridgeDispatcher.MaxTryItBytes);
        Assert.Equal(7, JsonDocument.Parse(Make().Handle(Origin, true, message)!).RootElement.GetProperty("payload").GetProperty("run").GetInt32());
    }

    [Fact]
    public void A_draft_over_the_character_limit_is_too_large()
    {
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Make().Handle(Origin, true, Run(new string('x', BridgeDispatcher.MaxTryItChars + 1)))));
        Assert.Empty(_api.Runs);
    }

    [Fact]
    public void A_message_over_the_try_it_byte_limit_is_too_large_before_parsing()
    {
        var message = Run("x") + new string(' ', BridgeDispatcher.MaxTryItBytes);
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Make().Handle(Origin, true, message)));
        Assert.Empty(_api.Runs);
    }

    [Fact]
    public void The_bigger_limit_is_for_try_it_only()
    {
        // Over the default size and naming tryit.run, but for another command: refused.
        var big = new string('x', BridgeDispatcher.MaxMessageBytes + 10);
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Make().Handle(Origin, true, Msg("pause.set", $$"""{"paused":true,"pad":"{{big}}"}"""))));
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Make().Handle(Origin, true, Msg("theme.set", $$"""{"mode":"{{big}} \"tryit.run\""}"""))));
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Make().Handle(Origin, true, Msg("tryit.cancel", $$"""{"pad":"{{big}}","note":"\"tryit.run\""}"""))));
        Assert.False(_api.Paused);
        Assert.Equal(0, _api.Cancels);
    }

    [Fact]
    public void Try_it_calls_are_rate_limited_like_any_other()
    {
        var d = Make();
        for (var i = 0; i < BridgeDispatcher.MaxMessagesPerWindow; i++) d.Handle(Origin, true, Run("hello"));
        Assert.Equal(BridgeError.RateLimited, ErrorOf(d.Handle(Origin, true, Run("hello"))));
        _now += BridgeDispatcher.WindowMs;
        Assert.NotNull(d.Handle(Origin, true, Run("hello")));
    }

    [Fact]
    public void Try_it_from_another_origin_is_dropped()
    {
        Assert.Null(Make().Handle("https://evil.example/", true, Run("hello")));
        Assert.Empty(_api.Runs);
    }

    [Fact]
    public void The_log_line_for_a_run_has_no_draft_text()
    {
        Make().Handle(Origin, true, Run($"please fix {Marker}"));
        Assert.All(_log, l => Assert.DoesNotContain(Marker, l));
        Assert.Contains("cmd=tryit.run", _log.Single());
    }

    [Fact]
    public void Cancel_reaches_the_app_and_takes_no_payload()
    {
        Assert.NotNull(Make().Handle(Origin, true, Msg("tryit.cancel")));
        Assert.Equal(1, _api.Cancels);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("tryit.cancel", """{"x":1}"""))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pause_set_applies_the_flag_and_reports_the_real_state(bool paused)
    {
        var reply = Make().Handle(Origin, true, Msg("pause.set", $$"""{"paused":{{paused.ToString().ToLowerInvariant()}}}"""));
        Assert.Equal(paused, _api.Paused);
        Assert.Equal(paused, JsonDocument.Parse(reply!).RootElement.GetProperty("payload").GetProperty("paused").GetBoolean());
    }

    [Theory]
    [InlineData("""{"paused":"true"}""")]
    [InlineData("""{"paused":1}""")]
    [InlineData("""{"paused":true,"x":1}""")]
    [InlineData("""{}""")]
    public void Pause_set_with_a_bad_payload_changes_nothing(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("pause.set", payload))));
        Assert.False(_api.Paused);
    }

    [Fact]
    public void Clipboard_copy_takes_no_text_from_the_page()
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("clipboard.copy", """{"text":"x"}"""))));
        _api.CanCopy = false;
        Assert.False(JsonDocument.Parse(Make().Handle(Origin, true, Msg("clipboard.copy"))!).RootElement.GetProperty("payload").GetProperty("copied").GetBoolean());
    }

    [Fact]
    public void Home_get_never_carries_a_key_and_takes_no_payload()
    {
        var reply = Make().Handle(Origin, true, Msg("home.get"));
        Assert.DoesNotContain("apiKey", reply!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Make().Handle(Origin, true, Msg("home.get", """{"x":1}"""))));
    }

    [Fact]
    public void The_command_list_has_no_way_to_paste_type_or_submit()
    {
        var forbidden = new[] { "paste", "replace", "send", "key", "submit", "type" };
        Assert.All(BridgeDispatcher.CommandNames, name => Assert.DoesNotContain(name.Split('.').Last(), forbidden));
    }

    // --- errors ---

    public static IEnumerable<object[]> Failures() =>
    [
        [new MissingKeyException("Groq", null), TryItErrors.NoKey],
        [new OptimizeException(OptimizeFailure.Empty, "m"), TryItErrors.Empty],
        [new OptimizeException(OptimizeFailure.TooLong, "m"), TryItErrors.TooLong],
        [new OptimizeException(OptimizeFailure.NoEndpoint, "m"), TryItErrors.NoModel],
        [new OptimizeException(OptimizeFailure.BadResponse, "m"), TryItErrors.BadResponse],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.Auth, "m")), TryItErrors.KeyRejected],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.RateLimit, "m")), TryItErrors.RateLimit],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.Network, "m")), TryItErrors.Offline],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.Timeout, "m")), TryItErrors.Timeout],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.Server, "m")), TryItErrors.ProviderError],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.BadRequest, "m")), TryItErrors.ProviderError],
        [new OptimizeException(OptimizeFailure.Model, "m", new LlmException(LlmErrorKind.Cancelled, "m")), TryItErrors.Cancelled],
        [new OperationCanceledException(), TryItErrors.Cancelled],
        [new InvalidOperationException("m"), TryItErrors.Failed],
    ];

    [Theory]
    [MemberData(nameof(Failures))]
    public void Existing_failures_map_to_plain_kinds(Exception ex, string kind) => Assert.Equal(kind, TryItErrors.KindOf(ex));

    [Fact]
    public void The_event_for_a_failure_never_carries_provider_or_exception_text()
    {
        var ex = new OptimizeException(OptimizeFailure.Model, $"{Marker} sk-live-123", new LlmException(LlmErrorKind.BadRequest, $"{Marker} provider said"));
        var json = BridgeEvents.TryItFailed(3, TryItErrors.KindOf(ex));
        Assert.DoesNotContain(Marker, json);
        Assert.DoesNotContain("sk-live", json);
    }

    [Fact]
    public void Every_kind_has_its_own_plain_message_and_an_unknown_kind_gets_the_generic_one()
    {
        Assert.All(TryItErrors.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(TryItErrors.MessageFor(k))));
        Assert.Equal(TryItErrors.Kinds.Count, TryItErrors.Kinds.Select(TryItErrors.MessageFor).Distinct().Count());
        Assert.Equal(TryItErrors.MessageFor(TryItErrors.Failed), TryItErrors.MessageFor("made up " + Marker));
    }

    // --- events and log ---

    [Theory]
    [InlineData("tryit.done.json")]
    [InlineData("tryit.failed.json")]
    public void Event_fixtures_match_what_the_app_sends(string file)
    {
        var f = JsonNode.Parse(File.ReadAllText(Path.Combine(BridgeContractTests.ContractsDir(), "events", file)))!;
        var run = f["run"]!.GetValue<int>();
        var actual = f["error"] is { } err
            ? BridgeEvents.TryItFailed(run, err.GetValue<string>())
            : BridgeEvents.TryItDone(run, f["output"].Deserialize<TryItOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        Assert.True(JsonNode.DeepEquals(f["event"], JsonNode.Parse(actual)), actual);
    }

    [Fact]
    public void The_run_log_line_holds_only_outcome_size_class_and_time()
    {
        Assert.Equal("tryit outcome=offline size=large ms=80", TryItLog.Describe(TryItErrors.Offline, 12_345, 80));
        Assert.Equal("tryit outcome=? size=small ms=1", TryItLog.Describe($"{Marker} text", 10, 1));
        Assert.Equal("small", TryItLog.SizeClass(499));
        Assert.Equal("medium", TryItLog.SizeClass(500));
        Assert.Equal("large", TryItLog.SizeClass(5_000));
    }

    [Fact]
    public void The_bridge_limits_are_what_the_plan_says()
    {
        Assert.Equal(4 * 1024, BridgeDispatcher.MaxMessageBytes);
        Assert.Equal(128 * 1024, BridgeDispatcher.MaxTryItBytes);
        Assert.True(BridgeDispatcher.MaxTryItChars <= new OptimizerOptions().MaxInputChars);
    }
}
