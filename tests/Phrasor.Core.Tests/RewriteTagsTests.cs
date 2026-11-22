using Phrasor.Core.Optimization;

namespace Phrasor.Core.Tests;

public class RewriteTagsTests
{
    [Theory]
    [InlineData("<rewrite>Explain closures.</rewrite>", "Explain closures.")]
    [InlineData("  <rewrite>\nExplain closures.\n</rewrite>\n", "Explain closures.")]
    [InlineData("<REWRITE>Explain closures.</Rewrite>", "Explain closures.")]
    public void A_wrapper_around_the_whole_prompt_is_removed(string prompt, string expected)
    {
        Assert.Equal(expected, RewriteTags.StripWrapper(prompt, "explain closures"));
    }

    [Theory]
    [InlineData("<rewrite>Explain closures.", "Explain closures.")]
    [InlineData("Explain closures.</rewrite>", "Explain closures.")]
    public void A_wrapper_with_only_one_tag_is_still_removed(string prompt, string expected)
    {
        Assert.Equal(expected, RewriteTags.StripWrapper(prompt, "explain closures"));
    }

    [Fact]
    public void A_prompt_without_the_wrapper_only_loses_surrounding_whitespace()
    {
        Assert.Equal("Explain closures.", RewriteTags.StripWrapper("\n  Explain closures.  \n", "explain closures"));
    }

    [Fact]
    public void A_tag_in_the_middle_of_the_prompt_is_kept()
    {
        var prompt = "Wrap the answer in <rewrite> tags, then stop.";

        Assert.Equal(prompt, RewriteTags.StripWrapper(prompt, "wrap the answer in tags"));
    }

    [Theory]
    [InlineData("<rewrite>make this shorter")]
    [InlineData("make this shorter</rewrite>")]
    [InlineData("make this <REWRITE> shorter")]
    public void A_wrapper_is_left_alone_when_the_draft_has_either_tag(string draft)
    {
        var prompt = "<rewrite>Make this shorter.</rewrite>";

        Assert.Equal(prompt, RewriteTags.StripWrapper(prompt, draft));
    }
}
