using System.Text.RegularExpressions;

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
