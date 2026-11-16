namespace Phrasor.Core.Theme;

/// <summary>
/// One set of colour tokens (dark or light) as plain 0xRRGGBB integers, so Core stays free of Windows types.
/// Both themes use the same token names. Values for window, rail, card, text, muted, accent, button and focus
/// come from the approved prototype; border and the status colours are chosen here. The hero tokens (the Home banner is
/// dark in both themes), field, glow and shadow serve the web pages. See-through shades (hover, selected, status
/// backgrounds) are mixed from these in the CSS, so no colour value lives outside this class.
/// </summary>
public sealed class Palette
{
    private readonly (string Name, int Rgb)[] _tokens;

    private Palette((string, int)[] tokens) => _tokens = tokens;

    public IReadOnlyList<(string Name, int Rgb)> Tokens => _tokens;

    public int this[string name] => _tokens.First(t => t.Name == name).Rgb;

    public static Palette Dark { get; } = new(
    [
        ("window", 0x181615),
        ("rail", 0x141211),
        ("card", 0x211E1C),
        ("border", 0x332E2B),
        ("text", 0xF6EBDD),
        ("muted", 0xA89C90),
        ("accent", 0xF58E4C),
        ("button", 0xF58E4C),
        ("button-text", 0x1B0F06),
        ("focus", 0xF7B27A),
        ("ok", 0x7CC48A),
        ("bad", 0xF2766B),
        ("warn", 0xF0B35A),
        ("field", 0x151210),
        ("hero-top", 0x2A2420),
        ("hero-bottom", 0x141211),
        ("hero-text", 0xFFE9D4),
        ("hero-accent", 0xF7A768),
        ("glow", 0xE8742A),
        ("shadow", 0x000000),
    ]);

    public static Palette Light { get; } = new(
    [
        ("window", 0xFFF8F0),
        ("rail", 0xFBEBD9),
        ("card", 0xFFFFFF),
        ("border", 0xE6D5C2),
        ("text", 0x181615),
        ("muted", 0x6B625B),
        ("accent", 0xB4531F),
        ("button", 0xF2813A),
        ("button-text", 0x181615),
        ("focus", 0xC2551A),
        ("ok", 0x2E7D4B),
        ("bad", 0xB3261E),
        ("warn", 0x8A5A00),
        ("field", 0xFFFFFF),
        ("hero-top", 0x2A2420),
        ("hero-bottom", 0x141211),
        ("hero-text", 0xFFE9D4),
        ("hero-accent", 0xF7A768),
        ("glow", 0xE8742A),
        ("shadow", 0x3C2410),
    ]);

    /// <summary>WCAG contrast ratio of two 0xRRGGBB colours: 1 (none) to 21 (black on white).</summary>
    public static double Contrast(int a, int b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo) (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(int rgb)
    {
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(rgb >> 16 & 0xFF) + 0.7152 * Channel(rgb >> 8 & 0xFF) + 0.0722 * Channel(rgb & 0xFF);
    }
}
