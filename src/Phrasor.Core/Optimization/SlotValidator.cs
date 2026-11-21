using System.Text.RegularExpressions;

namespace Phrasor.Core.Optimization;

public sealed record SlotCheck(IReadOnlyList<SlotReport> Slots, Readiness? Readiness, bool HasUnsupported);

/// <summary>
/// Holds the model to "only from the user's own words": a slot counts as filled only if its quote occurs in the draft.
/// A quote that does not is treated as missing and flagged, so the model cannot fill a slot from its own knowledge.
/// </summary>
public static partial class SlotValidator
{
    /// <summary>The report checked against the slot list of <paramref name="type"/>. A null report (the model sent none) gives no level.</summary>
    public static SlotCheck Check(string draft, TaskType type, IReadOnlyList<SlotReport>? reported)
    {
        if (reported is null) return new SlotCheck([], null, false);

        var haystack = Normalise(draft);
        var unsupported = false;
        var slots = new List<SlotReport>();
        foreach (var slot in SlotCatalog.For(type))
        {
            var report = reported.FirstOrDefault(r => r.Id == slot.Id);
            if (report is not { Status: SlotStatus.Filled, Quote: { } quote })
            {
                slots.Add(new SlotReport(slot.Id, SlotStatus.Missing, null));
                continue;
            }
            var needle = Normalise(quote);
            if (needle.Length > 0 && haystack.Contains(needle, StringComparison.Ordinal))
            {
                slots.Add(report);
                continue;
            }
            unsupported = true;
            slots.Add(new SlotReport(slot.Id, SlotStatus.Unsupported, null));
        }
        return new SlotCheck(slots, ReadinessRule.Compute(type, slots), unsupported);
    }

    /// <summary>Case, whitespace, curly quotes and held-back [[BLOCK_n]] markers don't count as differences.</summary>
    private static string Normalise(string text)
    {
        var plain = BlockMarker().Replace(text, " ")
            .Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"');
        return Whitespace().Replace(plain, " ").Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"\[\[BLOCK_\d+\]\]")]
    private static partial Regex BlockMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
