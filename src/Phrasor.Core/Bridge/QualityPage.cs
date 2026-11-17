using System.Text.Json;
using Phrasor.Core.Eval;

namespace Phrasor.Core.Bridge;

/// <summary>The choices the page sends to plan or start a run. Repeat is 1 to <see cref="QualityPage.MaxRepeat"/>.</summary>
public sealed record QualityRunOptions(int Repeat, bool Judge, bool IncludeHeldOut);

/// <summary>The chosen test set: a file name only (never a path) and counts only (never case text).</summary>
public sealed record QualityCasesView(string Name, int Cases, int HeldOut);

/// <summary>What the page may show of the judge: names, never keys. The key is only "set or not".</summary>
public sealed record QualityView(string JudgeProvider, string JudgeModel, bool JudgeKeySet, bool JudgeSameAsMain, QualityCasesView? Cases, bool Running, bool HasResults,
    IReadOnlyDictionary<string, string> DefaultModels);

/// <summary>What a run would cost, shown before it starts. Counts are of model requests, which the free plans limit.</summary>
public sealed record QualityPlanView(int Cases, int Runs, int Calls, int Minutes, int FreeDailyCalls, bool OverAllowance);

/// <summary>A run was asked for with nothing to run (no test set chosen, or no key). Carries no text.</summary>
public sealed class QualityNotReadyException : Exception;

public static class QualityPage
{
    public const int MaxRepeat = 5;
    public const int MaxModelChars = 100;

    /// <summary>Groq's free plan: 1,000 requests a day per model. Gemini's differs; this is the stricter one, so the warning errs early.</summary>
    public const int FreeDailyCalls = 1_000;

    // The runner waits 8 s after each run by default; a judge call adds a few more.
    private const int SecondsPerRun = 10, SecondsPerJudgedRun = 15;

    public static QualityPlanView Plan(IReadOnlyList<EvalCase> cases, QualityRunOptions o)
    {
        var selected = EvalRunner.SelectCases(cases, o.IncludeHeldOut);
        var runs = selected.Sum(c => EvalRunner.RunsFor(c, o.Repeat));
        var calls = o.Judge ? runs * 2 : runs;
        var minutes = (int)Math.Ceiling(runs * (o.Judge ? SecondsPerJudgedRun : SecondsPerRun) / 60.0);
        return new QualityPlanView(selected.Count, runs, calls, minutes, FreeDailyCalls, calls > FreeDailyCalls);
    }

    /// <summary>Passed, errored and total runs read from a finished results.json. Counts only: no draft or rewrite is kept.</summary>
    public static (int Passed, int Errored, int Total) Summarise(string resultsJson)
    {
        using var doc = JsonDocument.Parse(resultsJson);
        int passed = 0, errored = 0, total = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            total++;
            if (e.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String) errored++;
            else if (e.TryGetProperty("Passed", out var p) && p.ValueKind == JsonValueKind.True) passed++;
        }
        return (passed, errored, total);
    }
}

/// <summary>
/// Turns the runner's progress lines into a count. The runner reports one label as each run starts, a line while it waits out a
/// rate limit, and "stopped: ..." at the end. The text itself never leaves this class, so a case id cannot reach the page or the log.
/// </summary>
public sealed class QualityProgress
{
    public int Current { get; private set; }
    public bool Waiting { get; private set; }

    /// <summary>The provider's limit ended the run early.</summary>
    public bool Stopped { get; private set; }

    public void Update(string line)
    {
        if (line.StartsWith("stopped", StringComparison.Ordinal)) { Stopped = true; return; }
        // The end-of-run summaries are not runs starting.
        if (line.StartsWith("done:", StringComparison.Ordinal) || line.StartsWith("failed:", StringComparison.Ordinal) || line.StartsWith("cancelled", StringComparison.Ordinal)) return;
        if (line.Contains(" hit its rate limit", StringComparison.Ordinal)) { Waiting = true; return; }
        Current++;
        Waiting = false;
    }
}

/// <summary>How a run ended, as shown on the page.</summary>
public static class QualityState
{
    public const string Done = "done";
    public const string Stopped = "stopped"; // the provider's limit ended it early; finished runs are saved
    public const string Cancelled = "cancelled"; // the user stopped it; nothing is saved
    public const string Failed = "failed";
}
