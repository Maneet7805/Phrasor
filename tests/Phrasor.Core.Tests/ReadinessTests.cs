using Phrasor.Core.Optimization;

namespace Phrasor.Core.Tests;

public class ReadinessTests
{
    private static SlotReport Filled(string id, string quote = "x") => new(id, SlotStatus.Filled, quote);
    private static SlotReport Missing(string id) => new(id, SlotStatus.Missing, null);

    // ---- level rule ----

    [Fact]
    public void A_missing_required_slot_is_red()
    {
        Assert.Equal(Readiness.Red, ReadinessRule.Compute(TaskType.Study, [Missing("scope"), Filled("audience")]));
    }

    [Fact]
    public void A_required_slot_that_was_never_reported_is_red()
    {
        Assert.Equal(Readiness.Red, ReadinessRule.Compute(TaskType.Writing, [Filled("audience")]));
    }

    [Theory]
    [InlineData(TaskType.Coding)]
    [InlineData(TaskType.Research)]
    [InlineData(TaskType.Writing)]
    [InlineData(TaskType.Image)]
    [InlineData(TaskType.General)]
    public void Only_a_filled_scope_makes_green_while_nothing_counts_for_amber(TaskType type)
    {
        Assert.Equal(Readiness.Green, ReadinessRule.Compute(type, [Filled("scope")]));
    }

    [Fact]
    public void Flipping_counts_for_amber_on_one_slot_changes_the_level_for_exactly_that_slot()
    {
        var catalog = new List<Slot>
        {
            new("scope", "Scope", SlotKind.Fill, Required: true, CountsForAmber: false, UsedInRewrite: false, "what it covers"),
            new("audience", "Audience", SlotKind.Fill, Required: false, CountsForAmber: true, UsedInRewrite: false, "who it is for"),
            new("tone", "Tone", SlotKind.Fill, Required: false, CountsForAmber: false, UsedInRewrite: false, "the tone"),
        };

        Assert.Equal(Readiness.Amber, ReadinessRule.Compute(catalog, [Filled("scope"), Missing("audience"), Missing("tone")]));
        Assert.Equal(Readiness.Green, ReadinessRule.Compute(catalog, [Filled("scope"), Filled("audience"), Missing("tone")]));
        Assert.Equal(Readiness.Red, ReadinessRule.Compute(catalog, [Missing("scope"), Missing("audience")]));
    }

    [Fact]
    public void An_unsupported_slot_counts_as_missing()
    {
        Assert.Equal(Readiness.Red, ReadinessRule.Compute(TaskType.General, [new SlotReport("scope", SlotStatus.Unsupported, null)]));
    }

    // ---- provenance check ----

    [Fact]
    public void A_quote_found_in_the_draft_is_kept_even_with_other_case_quotes_and_spacing()
    {
        var check = SlotValidator.Check("Write it for MY boss,\n  she doesn’t like “long” emails", TaskType.Writing,
            [Filled("scope", "write it for my boss"), Filled("tone", "doesn't like \"long\"   emails")]);

        Assert.Equal(SlotStatus.Filled, check.Slots.Single(s => s.Id == "scope").Status);
        Assert.Equal(SlotStatus.Filled, check.Slots.Single(s => s.Id == "tone").Status);
        Assert.False(check.HasUnsupported);
        Assert.Equal(Readiness.Green, check.Readiness);
    }

    [Fact]
    public void A_filled_slot_whose_quote_is_not_in_the_draft_becomes_unsupported_and_lowers_the_level()
    {
        var check = SlotValidator.Check("explain databases", TaskType.Study, [Filled("scope", "for complete beginners")]);

        var scope = check.Slots.Single(s => s.Id == "scope");
        Assert.Equal(SlotStatus.Unsupported, scope.Status);
        Assert.Null(scope.Quote);
        Assert.True(check.HasUnsupported);
        Assert.Equal(Readiness.Red, check.Readiness);
    }

    [Fact]
    public void Block_markers_are_ignored_when_matching()
    {
        var check = SlotValidator.Check("fix this crash\n\n[[BLOCK_1]]", TaskType.Debugging, [Filled("scope", "fix this crash")]);

        Assert.Equal(Readiness.Green, check.Readiness);
    }

    [Fact]
    public void Unknown_slots_are_dropped_and_skipped_catalog_slots_are_added_as_missing()
    {
        var check = SlotValidator.Check("write a poem", TaskType.Writing, [Filled("scope", "write a poem"), Filled("role", "write")]);

        Assert.Equal(SlotCatalog.For(TaskType.Writing).Select(s => s.Id), check.Slots.Select(s => s.Id));
        Assert.Equal(SlotStatus.Missing, check.Slots.Single(s => s.Id == "audience").Status);
    }

    [Fact]
    public void No_report_means_no_level()
    {
        var check = SlotValidator.Check("explain databases", TaskType.Study, null);

        Assert.Null(check.Readiness);
        Assert.Empty(check.Slots);
        Assert.False(check.HasUnsupported);
    }

    // ---- log line ----

    [Fact]
    public void The_log_summary_holds_the_level_and_slot_ids_but_never_a_quote()
    {
        var draft = "tell my landlord the boiler is broken";
        var check = SlotValidator.Check(draft, TaskType.Writing, [Filled("scope", "the boiler is broken"), Filled("tone", "never in the draft")]);
        var line = ReadinessRule.LogSummary(check.Readiness, check.Slots);

        Assert.Contains("readiness=Green", line);
        Assert.Contains("scope:Filled", line);
        Assert.Contains("tone:Unsupported", line);
        Assert.DoesNotContain("boiler", line);
        Assert.DoesNotContain("landlord", line);
        Assert.DoesNotContain("never in the draft", line);
    }

    [Fact]
    public void The_log_summary_says_none_when_the_model_reported_no_slots()
    {
        Assert.Equal("readiness=none slots=-", ReadinessRule.LogSummary(null, []));
    }
}
