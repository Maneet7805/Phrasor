using Phrasor.Core.Theme;

namespace Phrasor.Core.Tests;

public class PaletteTests
{
    private const double ReadableText = 4.5;

    public static TheoryData<string> Themes => ["dark", "light"];

    private static Palette Get(string name) => name == "dark" ? Palette.Dark : Palette.Light;

    [Fact]
    public void Light_and_dark_define_the_same_token_names()
    {
        Assert.Equal(Palette.Dark.Tokens.Select(t => t.Name), Palette.Light.Tokens.Select(t => t.Name));
    }

    [Fact]
    public void Token_names_are_unique_and_values_are_valid_colours()
    {
        foreach (var p in new[] { Palette.Dark, Palette.Light })
        {
            Assert.Equal(p.Tokens.Count, p.Tokens.Select(t => t.Name).Distinct().Count());
            Assert.All(p.Tokens, t => Assert.InRange(t.Rgb, 0, 0xFFFFFF));
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_is_readable_on_window_rail_and_card(string theme)
    {
        var p = Get(theme);
        foreach (var surface in new[] { "window", "rail", "card" })
            Assert.True(Palette.Contrast(p["text"], p[surface]) >= ReadableText, $"{theme} text on {surface}");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Muted_and_accent_text_are_readable_on_window_and_card(string theme)
    {
        var p = Get(theme);
        foreach (var surface in new[] { "window", "card" })
        {
            Assert.True(Palette.Contrast(p["muted"], p[surface]) >= ReadableText, $"{theme} muted on {surface}");
            Assert.True(Palette.Contrast(p["accent"], p[surface]) >= ReadableText, $"{theme} accent on {surface}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Button_text_is_readable_on_the_button(string theme)
    {
        var p = Get(theme);
        Assert.True(Palette.Contrast(p["button-text"], p["button"]) >= ReadableText);
    }

    [Fact]
    public void Contrast_of_black_on_white_is_21_and_of_a_colour_with_itself_is_1()
    {
        Assert.Equal(21, Palette.Contrast(0x000000, 0xFFFFFF), 6);
        Assert.Equal(1, Palette.Contrast(0x123456, 0x123456), 6);
    }

    [Fact]
    public void Dark_window_is_the_brand_warm_black()
    {
        Assert.Equal(0x181615, Palette.Dark["window"]);
    }
}
