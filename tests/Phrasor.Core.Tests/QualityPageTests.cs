using System.Text.Json;
using System.Text.Json.Nodes;
using Phrasor.Core.Bridge;
using Phrasor.Core.Eval;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class QualityPageTests
{
    private readonly BridgeDispatcherTests.FakeApi _api = new();
    private readonly List<string> _log = [];

    private string? Send(string type, string? payload = null) =>
        new BridgeDispatcher(_api, _log.Add, () => 0).Handle(WebSecurityPolicy.StartUrl, true,
            $$"""{"id":"a1","type":"{{type}}"{{(payload is null ? "" : ",\"payload\":" + payload)}}}""");

    private static string? ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;

    private const string Options = """{"repeat":2,"judge":true,"heldOut":true}""";
    private const string Confirmed = """{"repeat":2,"judge":true,"heldOut":true,"confirm":true}""";

    private static EvalCase Case(string id, bool heldOut = false, int repeat = 1) =>
        new() { Id = id, Category = "c", Draft = "text", HeldOut = heldOut, Repeat = repeat };

    [Fact]
    public void Plan_counts_runs_and_requests_and_leaves_held_out_cases_out_unless_asked()
    {
        var cases = new[] { Case("a"), Case("b"), Case("h", heldOut: true), Case("min3", repeat: 3) };
        var plan = QualityPage.Plan(cases, new QualityRunOptions(2, Judge: false, IncludeHeldOut: false));
        Assert.Equal((3, 2 + 2 + 3, 7), (plan.Cases, plan.Runs, plan.Calls));
        var judged = QualityPage.Plan(cases, new QualityRunOptions(2, Judge: true, IncludeHeldOut: true));
        Assert.Equal((4, 9, 18), (judged.Cases, judged.Runs, judged.Calls));
    }

    [Fact]
    public void Plan_warns_when_the_free_daily_allowance_would_be_passed()
    {
        var many = Enumerable.Range(0, 400).Select(i => Case("c" + i)).ToList();
        Assert.True(QualityPage.Plan(many, new QualityRunOptions(3, true, false)).OverAllowance);
        Assert.False(QualityPage.Plan(many.Take(10).ToList(), new QualityRunOptions(3, false, false)).OverAllowance);
    }

    [Fact]
    public void Progress_counts_runs_and_never_keeps_the_runner_text()
    {
        var p = new QualityProgress();
        p.Update("case-one (run 1 of 3)");
        p.Update("case-one (run 1 of 3): the judge hit its rate limit, waiting 20 s");
        Assert.Equal((1, true), (p.Current, p.Waiting));
        p.Update("case-one (run 2 of 3)");
        p.Update("stopped: out of requests");
        Assert.Equal((2, false), (p.Current, p.Waiting));
    }

    [Fact]
    public void Summary_counts_passed_errored_and_total_runs()
    {
        var json = """[{"Id":"a","Passed":true},{"Id":"b","Passed":false},{"Id":"c","Passed":false,"Error":"x"}]""";
        Assert.Equal((1, 1, 3), QualityPage.Summarise(json));
    }

    [Fact]
    public void Events_match_what_the_page_reads()
    {
        Assert.Equal("""{"event":"quality","run":5,"state":"running","current":2,"total":9,"waiting":false}""", BridgeEvents.QualityProgress(5, 2, 9, false));
        Assert.Equal("""{"event":"quality","run":5,"state":"done","passed":7,"errored":1,"total":9}""", BridgeEvents.QualityFinished(5, QualityState.Done, 7, 1, 9));
    }

    [Fact]
    public void The_settings_commands_reach_the_app_with_clean_values()
    {
        Assert.Null(ErrorOf(Send("quality.setJudge", """{"provider":"groq","model":"  some-model  "}""")));
        Assert.Null(ErrorOf(Send("quality.plan", Options)));
        Assert.Equal(["judge:groq:some-model", "plan:QualityRunOptions { Repeat = 2, Judge = True, IncludeHeldOut = True }"], _api.QualityCalls);
    }

    [Theory]
    [InlineData("""{"provider":"custom","model":"m"}""")]
    [InlineData("""{"provider":"groq","model":""}""")]
    [InlineData("""{"provider":"groq","model":"a\nb"}""")]
    [InlineData("""{"provider":"groq","model":"m","extra":1}""")]
    [InlineData("""{"provider":"groq"}""")]
    public void A_bad_judge_is_refused(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("quality.setJudge", payload)));
        Assert.Empty(_api.QualityCalls);
    }

    [Theory]
    [InlineData("""{"repeat":0,"judge":true,"heldOut":false}""")]
    [InlineData("""{"repeat":6,"judge":true,"heldOut":false}""")]
    [InlineData("""{"repeat":1.5,"judge":true,"heldOut":false}""")]
    [InlineData("""{"repeat":"3","judge":true,"heldOut":false}""")]
    [InlineData("""{"repeat":3,"judge":"yes","heldOut":false}""")]
    [InlineData("""{"repeat":3,"judge":true}""")]
    [InlineData("""{"repeat":3,"judge":true,"heldOut":false,"confirm":true}""")] // confirm belongs to run, not plan
    public void Bad_options_are_refused(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("quality.plan", payload)));
        Assert.Empty(_api.QualityCalls);
    }

    [Fact]
    public void A_run_never_starts_without_confirm_true()
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("quality.run", Options)));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("quality.run", """{"repeat":2,"judge":true,"heldOut":true,"confirm":false}""")));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("quality.run", """{"repeat":2,"judge":true,"heldOut":true,"confirm":"true"}""")));
        Assert.Empty(_api.QualityCalls);
        Assert.Null(ErrorOf(Send("quality.run", Confirmed)));
        Assert.Single(_api.QualityCalls);
    }

    [Fact]
    public void A_second_run_is_busy_and_a_run_with_nothing_to_run_is_not_ready()
    {
        _api.NextQualityRun = null;
        Assert.Equal(BridgeError.Busy, ErrorOf(Send("quality.run", Confirmed)));
        _api.QualityReady = false;
        Assert.Equal(BridgeError.NotReady, ErrorOf(Send("quality.run", Confirmed)));
        Assert.Equal(BridgeError.NotReady, ErrorOf(Send("quality.plan", Options)));
    }

    [Fact]
    public void Cancel_choose_open_and_get_take_no_payload()
    {
        foreach (var type in new[] { "quality.cancel", "quality.chooseCases", "quality.openResults", "quality.get" })
        {
            Assert.Equal(BridgeError.BadRequest, ErrorOf(Send(type, """{"path":"C:\\x"}""")));
            Assert.Null(ErrorOf(Send(type)));
        }
        Assert.Equal(["cancel", "choose", "open"], _api.QualityCalls);
    }

    [Fact]
    public void The_log_never_holds_a_path_or_a_model_name()
    {
        Send("quality.setJudge", """{"provider":"groq","model":"MODEL-MARKER"}""");
        Send("quality.run", Confirmed);
        Send("quality.run", """{"repeat":2,"judge":true,"heldOut":true,"confirm":false,"x":"PATH-MARKER"}""");
        Assert.DoesNotContain(_log, l => l.Contains("MARKER"));
        Assert.All(_log, l => Assert.StartsWith("bridge cmd=", l));
    }

    [Fact]
    public void Fixtures_for_the_quality_page_exist()
    {
        var names = Directory.GetFiles(BridgeContractTests.ContractsDir(), "*quality*.json").Select(f => JsonNode.Parse(File.ReadAllText(f))!["request"]!["type"]!.GetValue<string>());
        Assert.Contains("quality.run", names);
    }
}
