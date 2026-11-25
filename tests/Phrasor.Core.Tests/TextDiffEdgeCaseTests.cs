using Phrasor.Core.Diffing;

namespace Phrasor.Core.Tests;

public class TextDiffEdgeCaseTests
{
    [Fact]
    public void Windows_line_endings_are_not_shown_as_changes()
    {
        var segments = TextDiff.Compute("line one\r\nline two", "line one\nline two");

        var only = Assert.Single(segments);
        Assert.Equal(DiffKind.Equal, only.Kind);
        Assert.Equal("line one\nline two", only.Text);
    }

    [Fact]
    public void Empty_original_is_all_inserted()
    {
        var segments = TextDiff.Compute("", "brand new prompt");

        var only = Assert.Single(segments);
        Assert.Equal(DiffKind.Inserted, only.Kind);
        Assert.Equal("brand new prompt", only.Text);
        Assert.Equal(1, TextDiff.ChangedFraction(segments));
    }

    [Fact]
    public void Empty_rewrite_is_all_deleted()
    {
        var segments = TextDiff.Compute("old draft", "");

        var only = Assert.Single(segments);
        Assert.Equal(DiffKind.Deleted, only.Kind);
        Assert.Equal("old draft", only.Text);
    }

    [Fact]
    public void Changed_fraction_is_zero_for_empty_or_deleted_only_input()
    {
        Assert.Equal(0, TextDiff.ChangedFraction(Array.Empty<DiffSegment>()));
        Assert.Equal(0, TextDiff.ChangedFraction(TextDiff.Compute("old draft", "")));
    }

    [Fact]
    public void Changed_fraction_counts_only_the_inserted_share_of_the_rewrite()
    {
        var segments = new[]
        {
            new DiffSegment(DiffKind.Equal, "abc"),
            new DiffSegment(DiffKind.Deleted, "zzzzzzzz"),
            new DiffSegment(DiffKind.Inserted, "d"),
        };

        Assert.Equal(0.25, TextDiff.ChangedFraction(segments));
    }
}
