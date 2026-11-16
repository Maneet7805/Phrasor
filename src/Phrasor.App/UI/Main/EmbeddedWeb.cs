using Phrasor.Core.Theme;
using Phrasor.Core.Web;

namespace Phrasor.App.UI.Main;

/// <summary>Serves the files under <c>Web/</c> that are embedded in the exe, plus the generated theme.css. Nothing is read from disk.</summary>
internal static class EmbeddedWeb
{
    private const string ResourcePrefix = "web/";
    private const string ThemeFile = "theme.css";

    /// <summary>The body and Content-Type for an embedded path, or null when there is no such file or it is not a type we serve.</summary>
    public static (byte[] Body, string ContentType)? Find(string path)
    {
        var contentType = WebSecurityPolicy.ContentTypeFor(path);
        if (contentType is null) return null;

        if (path == ThemeFile)
            return (System.Text.Encoding.UTF8.GetBytes(ThemeCss.Build(Palette.Light, Palette.Dark)), contentType);

        // Names are web/<path>; the build may write folders with a backslash.
        var assembly = typeof(EmbeddedWeb).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                                                          && n[ResourcePrefix.Length..].Replace('\\', '/') == path);
        if (name is null) return null;
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return (copy.ToArray(), contentType);
    }
}
