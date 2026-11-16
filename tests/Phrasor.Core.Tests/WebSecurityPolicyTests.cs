using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class WebSecurityPolicyTests
{
    [Theory]
    [InlineData("https://ui.phrasor.test/index.html")]
    [InlineData("https://ui.phrasor.test/")]
    [InlineData("https://UI.Phrasor.Test/theme.css?x=1")]
    public void Our_own_origin_is_allowed(string url) => Assert.True(WebSecurityPolicy.IsAllowedUrl(url));

    [Theory]
    [InlineData("http://ui.phrasor.test/index.html")]
    [InlineData("https://example.com/")]
    [InlineData("https://ui.phrasor.test.evil.com/")]
    [InlineData("https://evil.com/ui.phrasor.test")]
    [InlineData("https://ui.phrasor.test:8443/")]
    [InlineData("https://user@ui.phrasor.test/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://ui.phrasor.test/")]
    [InlineData("data:text/html,hi")]
    [InlineData("about:blank")]
    [InlineData("javascript:alert(1)")]
    [InlineData("wss://ui.phrasor.test/")]
    [InlineData("/index.html")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? url)
    {
        Assert.False(WebSecurityPolicy.IsAllowedUrl(url));
        Assert.Null(WebSecurityPolicy.FilePathFor(url));
    }

    [Theory]
    [InlineData("https://ui.phrasor.test/", "index.html")]
    [InlineData("https://ui.phrasor.test/index.html", "index.html")]
    [InlineData("https://ui.phrasor.test/theme.css?v=2#top", "theme.css")]
    [InlineData("https://ui.phrasor.test/fonts/inter.woff2", "fonts/inter.woff2")]
    public void File_path_comes_from_the_url_path(string url, string expected) =>
        Assert.Equal(expected, WebSecurityPolicy.FilePathFor(url));

    [Theory]
    [InlineData("https://ui.phrasor.test/a%5Cb")]
    [InlineData("https://ui.phrasor.test/C:/x")]
    public void Paths_with_backslashes_or_drive_letters_are_refused(string url) =>
        Assert.Null(WebSecurityPolicy.FilePathFor(url));

    // The URI parser collapses dot segments and double slashes itself, so these end up as plain paths inside the root.
    [Theory]
    [InlineData("https://ui.phrasor.test/../secret.txt", "secret.txt")]
    [InlineData("https://ui.phrasor.test/a/../../b", "b")]
    [InlineData("https://ui.phrasor.test/%2e%2e/x", "x")]
    [InlineData("https://ui.phrasor.test//x", "x")]
    public void Climbing_paths_never_leave_the_root(string url, string expected) =>
        Assert.Equal(expected, WebSecurityPolicy.FilePathFor(url));

    [Fact]
    public void Csp_denies_everything_by_default_and_has_no_wildcard_or_unsafe_keyword()
    {
        var csp = WebSecurityPolicy.ContentSecurityPolicy;
        Assert.StartsWith("default-src 'none'", csp);
        Assert.DoesNotContain("*", csp);
        Assert.DoesNotContain("unsafe", csp);
        Assert.DoesNotContain("http", csp);
        Assert.DoesNotContain("data:", csp);
    }

    [Fact]
    public void Csp_allows_only_same_origin_sources()
    {
        foreach (var directive in WebSecurityPolicy.ContentSecurityPolicy.Split(';', StringSplitOptions.TrimEntries))
        {
            var sources = directive.Split(' ').Skip(1);
            Assert.All(sources, s => Assert.Contains(s, new[] { "'none'", "'self'" }));
        }
    }

    [Fact]
    public void Every_response_carries_the_csp_and_no_store()
    {
        var headers = WebSecurityPolicy.ResponseHeaders("text/html; charset=utf-8").ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal(WebSecurityPolicy.ContentSecurityPolicy, headers["Content-Security-Policy"]);
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
    }

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("theme.css", "text/css; charset=utf-8")]
    [InlineData("fonts/a.WOFF2", "font/woff2")]
    public void Known_types_are_served(string path, string type) => Assert.Equal(type, WebSecurityPolicy.ContentTypeFor(path));

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("noextension")]
    public void Unknown_types_are_not_served(string path) => Assert.Null(WebSecurityPolicy.ContentTypeFor(path));
}
