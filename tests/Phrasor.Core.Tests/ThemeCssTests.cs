using System.Text.RegularExpressions;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Tests;

public class ThemeCssTests
{
    private static readonly string Css = ThemeCss.Build(Palette.Light, Palette.Dark);

    [Fact]
    public void Every_token_is_written_as_a_variable_in_both_themes()
    {
        var (light, dark) = Css.Split("@media", 2) is [var l, var d] ? (l, d) : throw new Xunit.Sdk.XunitException("no dark block");
        foreach (var (name, rgb) in Palette.Light.Tokens) Assert.Contains($"--{name}:#{rgb:X6};", light);
        foreach (var (name, rgb) in Palette.Dark.Tokens) Assert.Contains($"--{name}:#{rgb:X6};", dark);
    }

    [Fact]
    public void Light_and_dark_blocks_define_the_same_variable_names()
    {
        var parts = Css.Split("@media", 2);
        Assert.Equal(Names(parts[0]), Names(parts[1]));
        Assert.Equal(Palette.Light.Tokens.Count, Names(parts[0]).Count);
    }

    [Fact]
    public void Dark_values_sit_under_the_dark_colour_scheme_query()
    {
        Assert.Contains("@media (prefers-color-scheme:dark)", Css);
        Assert.StartsWith(":root{color-scheme:light dark;", Css);
    }

    private static List<string> Names(string block) => Regex.Matches(block, @"--([a-z-]+):#").Select(m => m.Groups[1].Value).ToList();
}
