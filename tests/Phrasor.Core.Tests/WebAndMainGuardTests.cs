using System.Text.RegularExpressions;
using Phrasor.Core.Theme;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

/// <summary>
/// The guardrail from decision D24: a text scan of what ships in the main window. Crude on purpose: it stops accidents,
/// not a determined author, so reviewers still read the web files.
/// </summary>
public class WebAndMainGuardTests
{
    private static readonly string[] ForbiddenInWeb =
    [
        "fetch(", "XMLHttpRequest", "WebSocket", "EventSource", "sendBeacon", "localStorage", "sessionStorage", "indexedDB",
        "console.", "eval(", "new Function", "innerHTML", "outerHTML", "document.write", "@import", "unsafe-inline", "unsafe-eval",
        "javascript:", "import(", "importScripts", "Worker(", "navigator.clipboard",
    ];

    private static readonly string[] ForbiddenInMain = ["TextBridge", "Keyboard.", "SendInput", "ClipboardHelper", "DelayedClipboard"];

    [Fact]
    public void Web_files_have_no_network_storage_eval_or_remote_code()
    {
        var files = WebFiles();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.All(ForbiddenInWeb, token => Assert.False(text.Contains(token, StringComparison.Ordinal), $"{name} contains '{token}'"));
            Assert.False(Regex.IsMatch(text, @"https?://|\bwss?://", RegexOptions.IgnoreCase), $"{name} contains a remote URL");
        }
    }

    [Fact]
    public void Web_files_hold_no_colour_values_only_theme_variables()
    {
        // Colours live in Palette (Core) and reach the page as theme.css variables; see-through shades are mixed from them.
        foreach (var file in WebFiles())
        {
            var text = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", "", RegexOptions.Singleline);
            if (!file.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", text);
            Assert.DoesNotMatch(@"\b(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch)\(", text);
            if (file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) Assert.DoesNotMatch(@"(?<![\w-])(white|black|red|green|blue|orange|gray|grey)(?![\w-])", text);
        }
    }

    [Fact]
    public void Every_css_variable_in_use_is_defined_by_the_theme_or_the_stylesheet()
    {
        var css = WebFiles().Where(f => f.EndsWith(".css")).Select(File.ReadAllText).ToList();
        var defined = Regex.Matches(ThemeCss.Build(Palette.Light, Palette.Dark), @"--([a-z0-9-]+):").Select(m => m.Groups[1].Value)
            .Concat(css.SelectMany(c => Regex.Matches(c, @"(?<![\w-])--([a-z0-9-]+)\s*:").Select(m => m.Groups[1].Value)))
            .ToHashSet();
        var used = css.SelectMany(c => Regex.Matches(c, @"var\(--([a-z0-9-]+)").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.NotEmpty(used);
        Assert.Empty(used.Except(defined));
    }

    [Fact]
    public void Files_the_pages_point_at_exist_and_are_served_types()
    {
        var web = Path.Combine(Root(), "src", "Phrasor.App", "Web");
        foreach (var file in WebFiles())
        {
            var text = File.ReadAllText(file);
            var refs = Regex.Matches(text, @"(?:src|href)=""(/[^""#]*)""|url\(""(/[^""]*)""\)")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            foreach (var path in refs.Select(r => r.TrimStart('/')).Where(r => r != "theme.css")) // theme.css is generated
            {
                Assert.True(File.Exists(Path.Combine(web, path)), $"{Path.GetFileName(file)} points at missing {path}");
                Assert.NotNull(WebSecurityPolicy.ContentTypeFor(path));
            }
        }
    }

    [Fact]
    public void Html_has_no_inline_script_style_or_event_handler()
    {
        foreach (var file in WebFiles().Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            foreach (Match tag in Regex.Matches(text, @"<script\b[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                Assert.Matches(@"\bsrc=""/[^""]*""", tag.Value);
                Assert.True(tag.Groups[1].Value.Trim().Length == 0, $"{name} has an inline script");
            }
            Assert.DoesNotMatch(@"<style\b", text);
            Assert.DoesNotMatch(@"\sstyle\s*=", text);
            Assert.DoesNotMatch(@"\son\w+\s*=", text);
        }
    }

    [Fact]
    public void Main_window_code_never_touches_text_capture_paste_or_keystrokes()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "src", "Phrasor.App", "UI", "Main"), "*.cs");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.All(ForbiddenInMain, token => Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains '{token}'"));
        }
    }

    // Shipped pages only: html, css and js outside contracts (test examples) and dev (not embedded).
    private static string[] WebFiles()
    {
        var web = Path.Combine(Root(), "src", "Phrasor.App", "Web");
        return Directory.GetFiles(web, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html") || f.EndsWith(".css") || f.EndsWith(".js"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}contracts{Path.DirectorySeparatorChar}"))
            .ToArray();
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Phrasor.App", "Web"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
