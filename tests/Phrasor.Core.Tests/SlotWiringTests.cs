using System.Text.Json;
using Phrasor.Core.Eval;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using static Phrasor.Core.Tests.PromptOptimizerTests;

namespace Phrasor.Core.Tests;

/// <summary>Parser, optimiser, eval and saved-results behaviour for the slot report (#266).</summary>
public class SlotWiringTests
{
    private static readonly LlmEndpoint Primary = new(ProviderPresets.Groq, "https://primary", "big-model", "k");

    private static string Reply(string prompt, string slotsJson, string task = "study", string verdict = "rewritten") =>
        $$"""{"task_type":"{{task}}","verdict":"{{verdict}}","prompt":{{JsonSerializer.Serialize(prompt)}},"changes":[],"questions":[]{{(slotsJson.Length == 0 ? "" : ",\"slots\":" + slotsJson)}}}""";

    private static Task<OptimizeResult> Run(string draft, string reply, OptimizeRequest? request = null) =>
        new PromptOptimizer(new FakeClient(_ => reply)).OptimizeAsync(request ?? new OptimizeRequest { Text = draft }, [Primary]);

    // ---- parser ----

    [Fact]
    public void Parses_slots_and_drops_unknown_ids_and_bad_statuses()
    {
        var parsed = ResponseParser.Parse(Reply("p", """
            [{"id":"scope","status":"filled","quote":"explain x"},{"id":"nonsense","status":"filled","quote":"a"},
             {"id":"audience","status":"maybe"},{"id":"end_goal","status":"missing"}]
            """));

        Assert.Equal(["scope", "audience", "end_goal"], parsed.Slots!.Select(s => s.Id));
        Assert.Equal(SlotStatus.Filled, parsed.Slots![0].Status);
        Assert.Equal("explain x", parsed.Slots[0].Quote);
        Assert.Equal(SlotStatus.Missing, parsed.Slots[1].Status);
    }

    [Fact]
    public void A_filled_slot_without_a_quote_becomes_missing()
    {
        var parsed = ResponseParser.Parse(Reply("p", """[{"id":"scope","status":"filled"},{"id":"audience","status":"filled","quote":"  "}]"""));

        Assert.All(parsed.Slots!, s => Assert.Equal(SlotStatus.Missing, s.Status));
    }

    [Fact]
    public void Old_replies_without_slots_still_parse_with_a_null_report()
    {
        Assert.Null(ResponseParser.Parse(Reply("p", "")).Slots);
        Assert.Null(ResponseParser.Parse(Reply("p", "\"not an array\"")).Slots);
    }

    [Fact]
    public void The_slot_list_is_capped_and_a_repeated_id_counts_once()
    {
        var many = string.Join(",", Enumerable.Range(0, 40).Select(_ => """{"id":"scope","status":"missing"}"""));
        var parsed = ResponseParser.Parse(Reply("p", $"[{many}]"));

        Assert.Single(parsed.Slots!);
    }

    // ---- optimiser ----

    [Fact]
    public async Task A_made_up_quote_is_demoted_and_warned_about()
    {
        var result = await Run("explain databases", Reply("Explain databases.", """[{"id":"scope","status":"filled","quote":"for complete beginners"}]"""));

        Assert.Equal(Readiness.Red, result.Readiness);
        Assert.Equal(SlotStatus.Unsupported, result.Slots.Single(s => s.Id == "scope").Status);
        Assert.Contains(result.Warnings, w => w.Contains("not in your draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_red_draft_gets_the_fixed_hint_and_the_rewrite_is_untouched()
    {
        var result = await Run("explain databases", Reply("Explain databases.", """[{"id":"scope","status":"missing"}]"""));

        Assert.Equal(Readiness.Red, result.Readiness);
        Assert.Equal("Explain databases.", result.Prompt);
        Assert.Contains(ReadinessRule.RedHint, result.Questions);
    }

    [Fact]
    public async Task A_green_draft_gets_no_hint_and_keeps_its_quote_in_memory_only()
    {
        var result = await Run("translate good morning to malay", Reply("Translate \"good morning\" to Malay.", """[{"id":"scope","status":"filled","quote":"translate good morning to malay"}]""", task: "general"));

        Assert.Equal(Readiness.Green, result.Readiness);
        Assert.DoesNotContain(ReadinessRule.RedHint, result.Questions);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task A_reply_without_slots_leaves_the_level_null()
    {
        var result = await Run("explain databases", Reply("Explain databases.", ""));

        Assert.Null(result.Readiness);
        Assert.Empty(result.Slots);
        Assert.DoesNotContain(ReadinessRule.RedHint, result.Questions);
    }

    [Fact]
    public async Task The_chosen_task_type_decides_which_slots_are_checked()
    {
        var result = await Run("write about my trip", Reply("Write about my trip.", """[{"id":"scope","status":"filled","quote":"write about my trip"}]""", task: "coding"),
            new OptimizeRequest { Text = "write about my trip", TaskOverride = TaskType.Writing });

        Assert.Equal(SlotCatalog.For(TaskType.Writing).Select(s => s.Id), result.Slots.Select(s => s.Id));
    }

    [Fact]
    public async Task The_level_survives_the_repair_pass_and_the_appended_requirements()
    {
        // "report_v2.pdf" is dropped by the first rewrite, so a repair call runs; its reply has no slots.
        var calls = 0;
        var client = new FakeClient(_ => ++calls == 1
            ? Reply("Summarise the report.", """[{"id":"scope","status":"filled","quote":"summarise report_v2.pdf"}]""")
            : Reply("Summarise report_v2.pdf.", ""));
        var result = await new PromptOptimizer(client).OptimizeAsync(new OptimizeRequest { Text = "summarise report_v2.pdf" }, [Primary]);
        var appended = RequirementsAppender.Apply(result, new Dictionary<string, string> { ["all"] = "Keep it short and plain." });

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal(Readiness.Green, appended.Readiness);
        Assert.Equal(SlotStatus.Filled, appended.Slots.Single(s => s.Id == "scope").Status);
        Assert.DoesNotContain(appended.Warnings, w => w.Contains("not in your draft", StringComparison.Ordinal));
    }

    // ---- system prompt ----

    [Fact]
    public void The_system_prompt_lists_every_slot_and_stays_one_deterministic_string()
    {
        var text = SystemPrompt.Text;

        foreach (var slot in Enum.GetValues<TaskType>().SelectMany(SlotCatalog.For).DistinctBy(s => s.Id))
            Assert.Contains(slot.Id, text);
        Assert.Contains("exact words copied from the draft", text);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("{{SLOTS}}", text);
        Assert.Same(SystemPrompt.Text, SystemPrompt.Text);
    }

    [Fact]
    public void Every_example_reports_slots_with_quotes_that_are_in_its_own_draft()
    {
        var examples = System.Text.RegularExpressions.Regex.Matches(SystemPrompt.Text, @"^Draft: ""(?<draft>.*)""\n(?<json>\{.*\})$", System.Text.RegularExpressions.RegexOptions.Multiline);

        Assert.Equal(8, examples.Count);
        foreach (System.Text.RegularExpressions.Match example in examples)
        {
            var parsed = ResponseParser.Parse(example.Groups["json"].Value);
            Assert.NotNull(parsed.Slots);
            var check = SlotValidator.Check(example.Groups["draft"].Value, parsed.TaskType, parsed.Slots);
            Assert.False(check.HasUnsupported, example.Groups["draft"].Value);
        }
    }

    // ---- eval and saved results ----

    private static OptimizeResult Result(Readiness? readiness) =>
        new() { Original = "d", Prompt = "p", TaskType = TaskType.General, Verdict = Verdict.LightEdit, Model = "m", Readiness = readiness };

    [Fact]
    public void A_wrong_level_fails_a_case_that_expects_one_and_a_missing_label_never_fails()
    {
        var expectRed = new EvalCase { Id = "a", Category = "c", Draft = "d", ExpectReadiness = Readiness.Red };

        Assert.Contains(EvalRunner.Grade(expectRed, Result(Readiness.Green)), f => f.Contains("readiness", StringComparison.Ordinal));
        Assert.Contains(EvalRunner.Grade(expectRed, Result(null)), f => f.Contains("readiness", StringComparison.Ordinal));
        Assert.DoesNotContain(EvalRunner.Grade(expectRed, Result(Readiness.Red)), f => f.Contains("readiness", StringComparison.Ordinal));
        Assert.DoesNotContain(EvalRunner.Grade(expectRed with { ExpectReadiness = null }, Result(null)), f => f.Contains("readiness", StringComparison.Ordinal));
        // Amber is reported, not graded, until a slot counts for amber.
        Assert.DoesNotContain(EvalRunner.Grade(expectRed with { ExpectReadiness = Readiness.Amber }, Result(Readiness.Green)), f => f.Contains("readiness", StringComparison.Ordinal));
    }

    [Fact]
    public void Case_files_read_expectReadiness_by_name_and_new_cases_are_labelled()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "eval", "cases.json"))) dir = Path.GetDirectoryName(dir);
        var cases = EvalRunner.LoadCases(Path.Combine(dir!, "eval", "cases.json")).Where(c => c.ExpectReadiness is not null).ToList();

        Assert.Equal(12, cases.Count);
        Assert.Equal(4, cases.Count(c => c.ExpectReadiness == Readiness.Red));
        Assert.Equal(4, cases.Count(c => c.ExpectReadiness == Readiness.Amber));
        Assert.Equal(4, cases.Count(c => c.ExpectReadiness == Readiness.Green));
        Assert.DoesNotContain(cases, c => c.HeldOut);
    }

    [Fact]
    public void Saved_results_keep_the_level_and_slot_ids_but_never_a_quote_and_old_files_still_load()
    {
        var result = Result(Readiness.Amber) with { Slots = [new SlotReport("scope", SlotStatus.Filled, "my secret words")] };
        var path = Path.Combine(Path.GetTempPath(), $"phrasor-slots-{Guid.NewGuid():N}.json");
        try
        {
            var json = SavedResults.ToJson([new EvalCaseResult { Case = new EvalCase { Id = "a", Category = "c", Draft = "d" }, Result = result }]);
            Assert.DoesNotContain("my secret words", json);
            File.WriteAllText(path, json);
            var back = SavedResults.ToResult(SavedResults.Load(path).Single(), "d")!;
            Assert.Equal(Readiness.Amber, back.Readiness);

            File.WriteAllText(path, """[{"Id":"a","Run":1,"Draft":"d","Output":"p","TaskType":"General","Verdict":"LightEdit"}]""");
            Assert.Null(SavedResults.ToResult(SavedResults.Load(path).Single(), "d")!.Readiness);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
