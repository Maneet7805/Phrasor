using Phrasor.Core.Optimization;

namespace Phrasor.Core.Tests;

public class SlotCatalogTests
{
    private static IEnumerable<TaskType> RealTypes => Enum.GetValues<TaskType>().Where(t => t != TaskType.Auto);

    [Fact]
    public void Every_task_type_has_slots_with_unique_ids_and_one_required_scope()
    {
        foreach (var type in RealTypes)
        {
            var slots = SlotCatalog.For(type);
            Assert.NotEmpty(slots);
            Assert.Equal(slots.Count, slots.Select(s => s.Id).Distinct().Count());
            Assert.Equal(["scope"], slots.Where(s => s.Required).Select(s => s.Id));
        }
    }

    [Fact]
    public void Auto_uses_the_general_list()
    {
        Assert.Equal(SlotCatalog.For(TaskType.General), SlotCatalog.For(TaskType.Auto));
    }

    [Fact]
    public void Every_slot_has_a_hint_and_a_lower_snake_case_id()
    {
        foreach (var slot in RealTypes.SelectMany(SlotCatalog.For))
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.Hint));
            Assert.False(string.IsNullOrWhiteSpace(slot.Label));
            Assert.Matches("^[a-z]+(_[a-z]+)*$", slot.Id);
        }
    }

    [Fact]
    public void Nothing_the_rules_forbid_adding_is_a_slot()
    {
        // The research notes' "never add" list: a role, a level, tests, examples, an invented length.
        string[] forbidden = ["role", "level", "persona", "tests", "examples", "example", "word_count", "length"];
        var ids = RealTypes.SelectMany(SlotCatalog.For).Select(s => s.Id).ToHashSet();

        Assert.DoesNotContain(ids, forbidden.Contains);
    }

    [Fact]
    public void Provisional_flags_start_off_until_the_study_turns_them_on()
    {
        var all = RealTypes.SelectMany(SlotCatalog.For).ToList();

        Assert.All(all, s => Assert.False(s.CountsForAmber));
        Assert.All(all, s => Assert.False(s.UsedInRewrite));
    }

    [Fact]
    public void Point_out_slots_are_never_required()
    {
        Assert.All(RealTypes.SelectMany(SlotCatalog.For).Where(s => s.Kind == SlotKind.PointOut), s => Assert.False(s.Required));
    }
}
