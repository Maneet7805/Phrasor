namespace Phrasor.Core.Optimization;

/// <summary>The level is a pure function of the slot table and the report, so it is tested with no model and the study changes it by flipping flags.</summary>
public static class ReadinessRule
{
    /// <summary>Shown for a red draft. Fixed text in code, never model text.</summary>
    public const string RedHint = "Add who it's for and what you want back.";

    public static Readiness Compute(TaskType type, IReadOnlyList<SlotReport> slots) => Compute(SlotCatalog.For(type), slots);

    /// <summary>Red when a required slot is not filled, else amber when a slot that counts for amber is not, else green. A slot the report skipped is not filled.</summary>
    public static Readiness Compute(IReadOnlyList<Slot> catalog, IReadOnlyList<SlotReport> slots)
    {
        bool NotFilled(Slot slot) => !slots.Any(s => s.Id == slot.Id && s.Status == SlotStatus.Filled);
        if (catalog.Any(s => s.Required && NotFilled(s))) return Readiness.Red;
        if (catalog.Any(s => s.CountsForAmber && NotFilled(s))) return Readiness.Amber;
        return Readiness.Green;
    }

    /// <summary>The level and each slot's id and status for the log. Never a quote or any other draft text.</summary>
    public static string LogSummary(Readiness? readiness, IReadOnlyList<SlotReport> slots) =>
        $"readiness={readiness?.ToString() ?? "none"} slots={(slots.Count == 0 ? "-" : string.Join(",", slots.Select(s => $"{s.Id}:{s.Status}")))}";
}
