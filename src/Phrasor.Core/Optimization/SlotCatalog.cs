namespace Phrasor.Core.Optimization;

/// <summary>Fill: only ever from the user's own words. PointOut: never filled, only flagged as a gap.</summary>
public enum SlotKind
{
    Fill,
    PointOut,
}

/// <summary>One thing a draft may say about what the answer should look like.</summary>
/// <param name="Id">Stable lower snake case id the model reports against.</param>
/// <param name="Label">The label of the line this slot would get in a rewrite.</param>
/// <param name="Required">A missing required slot makes the draft red.</param>
/// <param name="CountsForAmber">A missing slot with this set makes the draft amber. Off until the structure study says the slot helps.</param>
/// <param name="UsedInRewrite">The user's words go on a labelled line. Off until the structure study says it helps.</param>
/// <param name="Hint">Plain words for a gap, for example "who it is for".</param>
public sealed record Slot(string Id, string Label, SlotKind Kind, bool Required, bool CountsForAmber, bool UsedInRewrite, string Hint);

/// <summary>
/// The only place the slot list lives. Contents are provisional (#266): editing the rule after the structure study
/// means editing this table and re-running the tests. Nothing else names a slot.
/// </summary>
public static class SlotCatalog
{
    private static Slot Scope() => new("scope", "Scope", SlotKind.Fill, Required: true, CountsForAmber: false, UsedInRewrite: false, "what you want the answer to contain");

    private static Slot Fill(string id, string label, string hint) => new(id, label, SlotKind.Fill, false, false, false, hint);

    private static readonly Slot[] CodingSlots =
        [Scope(), Fill("constraints", "Constraints", "what must not change or break"), Fill("output_form", "Output form", "what form the answer should take"), Fill("language_tool", "Language or tool", "the language or tool")];

    private static readonly Slot[] LearningSlots =
        [Scope(), Fill("audience", "Audience", "who it is for"), Fill("output_form", "Output form", "what form the answer should take"), new("end_goal", "End goal", SlotKind.PointOut, false, false, false, "what you will use it for")];

    private static readonly Slot[] WritingSlots =
        [Scope(), Fill("audience", "Audience", "who it is for"), Fill("tone", "Tone", "the tone"), Fill("length_format", "Length and format", "the length or format")];

    private static readonly Slot[] ImageSlots =
        [Scope(), Fill("subject", "Subject", "what the picture shows"), Fill("style", "Style", "the style"), Fill("keep", "Keep", "what to keep when editing")];

    private static readonly Slot[] OtherSlots =
        [Scope(), Fill("target", "Target", "the language, length or format wanted")];

    /// <summary>Task types that share one slot list, in the order the system prompt shows them.</summary>
    internal static readonly (TaskType[] Types, Slot[] Slots)[] Groups =
    [
        ([TaskType.Coding, TaskType.Debugging], CodingSlots),
        ([TaskType.Research, TaskType.Study], LearningSlots),
        ([TaskType.Writing], WritingSlots),
        ([TaskType.Image], ImageSlots),
        ([TaskType.Planning, TaskType.General], OtherSlots),
    ];

    /// <summary>The slots for a task type. Auto (not decided yet) gets the general list.</summary>
    public static IReadOnlyList<Slot> For(TaskType type)
    {
        foreach (var (types, slots) in Groups)
            if (Array.IndexOf(types, type) >= 0) return slots;
        return OtherSlots;
    }

    /// <summary>Every id in any list: what the parser accepts before the task type is known for sure.</summary>
    internal static bool IsKnownId(string id) => Groups.Any(g => g.Slots.Any(s => s.Id == id));
}
