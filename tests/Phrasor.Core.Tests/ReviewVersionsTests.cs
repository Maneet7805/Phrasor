using Phrasor.Core.Optimization;
using Xunit;

namespace Phrasor.Core.Tests;

public class ReviewVersionsTests
{
    private const string Draft = "fix the login bug please";

    private static OptimizeResult Result(string prompt, string original = Draft) => new()
    {
        Original = original, Prompt = prompt, TaskType = TaskType.Coding, Verdict = Verdict.Rewritten, Model = "m",
    };

    private static VersionKey Key(TaskType t = TaskType.Auto, OptimizeMode m = OptimizeMode.Standard, string model = "groq/a") => new(t, m, model);

    [Fact]
    public void Same_key_returns_the_identical_stored_result()
    {
        var v = new ReviewVersions(Draft);
        var r = Result("Fix the login bug.");
        v.Put(Key(), r);
        Assert.True(v.TryGet(Key(), out var back));
        Assert.Same(r, back);
    }

    [Fact]
    public void Different_mode_task_or_model_are_different_versions()
    {
        var v = new ReviewVersions(Draft);
        v.Put(Key(), Result("a"));
        Assert.False(v.Has(Key(m: OptimizeMode.Light)));
        Assert.False(v.Has(Key(TaskType.Coding)));
        Assert.False(v.Has(Key(model: "groq/b")));
    }

    [Fact]
    public void Auto_and_a_chip_for_the_same_task_do_not_share_a_version()
    {
        var v = new ReviewVersions(Draft);
        v.Put(Key(TaskType.Auto), Result("auto text"));
        Assert.False(v.Has(Key(TaskType.Coding)));
    }

    [Fact]
    public void A_result_not_made_from_the_original_is_refused()
    {
        var v = new ReviewVersions(Draft);
        var ex = Assert.Throws<ArgumentException>(() => v.Put(Key(), Result("x", original: "an earlier rewrite")));
        Assert.DoesNotContain("earlier rewrite", ex.Message);
        Assert.Equal(0, v.Count);
    }

    [Fact]
    public void Put_on_an_existing_key_replaces_only_that_version_and_drops_its_edit()
    {
        var v = new ReviewVersions(Draft);
        var other = Key(m: OptimizeMode.Light);
        v.Put(Key(), Result("one"));
        v.Put(other, Result("light"));
        v.SetEdit(Key(), "my edit");
        v.SetEdit(other, "other edit");

        var again = Result("two");
        v.Put(Key(), again);

        Assert.Same(again, v.TryGet(Key(), out var r) ? r : null);
        Assert.Null(v.GetEdit(Key()));
        Assert.Equal("other edit", v.GetEdit(other));
        Assert.Equal(2, v.Count);
        Assert.Equal([Key(), other], v.Keys);
    }

    [Fact]
    public void An_edit_stays_with_its_version_when_the_user_switches_away_and_back()
    {
        var v = new ReviewVersions(Draft);
        v.Put(Key(), Result("one"));
        v.Put(Key(m: OptimizeMode.Detailed), Result("two"));
        v.SetEdit(Key(), "edited");
        Assert.Null(v.GetEdit(Key(m: OptimizeMode.Detailed)));
        Assert.Equal("edited", v.GetEdit(Key()));
        v.ClearEdit(Key());
        Assert.Null(v.GetEdit(Key()));
    }

    [Fact]
    public void An_edit_for_a_version_that_does_not_exist_is_ignored()
    {
        var v = new ReviewVersions(Draft);
        v.SetEdit(Key(), "x");
        Assert.Null(v.GetEdit(Key()));
    }

    [Fact]
    public void Clear_forgets_everything_because_the_input_changed()
    {
        var v = new ReviewVersions(Draft);
        v.Put(Key(), Result("one"));
        v.SetEdit(Key(), "edit");
        v.Clear();
        Assert.Equal(0, v.Count);
        Assert.False(v.Has(Key()));
        Assert.Null(v.GetEdit(Key()));
        Assert.Empty(v.Keys);
    }

    [Fact]
    public void ToString_never_prints_the_draft_or_a_rewrite()
    {
        var v = new ReviewVersions(Draft);
        v.Put(Key(), Result("secret rewrite text"));
        var s = v.ToString();
        Assert.DoesNotContain("login", s);
        Assert.DoesNotContain("secret", s);
    }
}
