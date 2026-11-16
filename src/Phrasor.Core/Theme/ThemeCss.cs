using System.Text;

namespace Phrasor.Core.Theme;

/// <summary>Writes the palette as CSS variables, so no colour value lives in the web files.</summary>
public static class ThemeCss
{
    /// <summary>Light values on <c>:root</c>, dark values under <c>prefers-color-scheme: dark</c>.</summary>
    public static string Build(Palette light, Palette dark)
    {
        var css = new StringBuilder();
        css.Append(":root{color-scheme:light dark;");
        AppendVariables(css, light);
        css.Append("}\n@media (prefers-color-scheme:dark){:root{");
        AppendVariables(css, dark);
        css.Append("}}\n");
        return css.ToString();
    }

    private static void AppendVariables(StringBuilder css, Palette palette)
    {
        foreach (var (name, rgb) in palette.Tokens)
            css.Append("--").Append(name).Append(":#").Append(rgb.ToString("X6")).Append(';');
    }
}
