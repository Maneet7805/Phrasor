namespace Phrasor.Core.Web;

/// <summary>
/// What the main window's embedded browser may load. The page lives under one made-up host that cannot resolve
/// (<c>.test</c> is reserved), is served from files inside the exe, and everything else is refused.
/// </summary>
public static class WebSecurityPolicy
{
    public const string Host = "ui.phrasor.test";
    public const string Origin = "https://" + Host;
    public const string StartUrl = Origin + "/index.html";
    public const string DefaultFile = "index.html";

    /// <summary>
    /// Everything is off except same-origin styles and scripts (files only: no inline script, no eval, no remote).
    /// Widen it only for something a page really needs (images and fonts come with the pages that use them);
    /// never add a wildcard, a remote host or an unsafe keyword.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>True for an http(s) URL on exactly our host and port, with no user info.</summary>
    public static bool IsAllowedUrl(string? url) => TryParse(url, out _);

    /// <summary>The embedded file a URL asks for ("index.html" for the root), or null if the URL is not ours or the path is odd.</summary>
    public static string? FilePathFor(string? url)
    {
        if (!TryParse(url, out var uri)) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (path.Length == 0) return DefaultFile;
        // Reject anything that could climb out of the embedded folder or confuse the lookup.
        if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(s => s is "" or "." or "..")) return null;
        return path;
    }

    /// <summary>The Content-Type for a served file, or null when the type is not one we serve.</summary>
    public static string? ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".woff2" => "font/woff2",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => null,
    };

    /// <summary>Headers on every response we serve.</summary>
    public static IReadOnlyList<(string Name, string Value)> ResponseHeaders(string contentType) =>
    [
        ("Content-Type", contentType),
        ("Content-Security-Policy", ContentSecurityPolicy),
        ("Cache-Control", "no-store"),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "no-referrer"),
    ];

    private static bool TryParse(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed is not null
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase)
            && parsed.IsDefaultPort
            && parsed.UserInfo.Length == 0)
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }
}
