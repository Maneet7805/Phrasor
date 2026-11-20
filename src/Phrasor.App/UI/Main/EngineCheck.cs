using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Phrasor.Core.Web;

namespace Phrasor.App.UI.Main;

/// <summary>
/// Asks whether the browser engine the main window needs is installed, and holds the one <see cref="EngineSource"/>.
/// Logs events only. The tray, hotkeys, review window and voice never call this.
/// </summary>
internal static class EngineCheck
{
    private const string FolderVariable = "PHRASOR_WEBVIEW2_FOLDER";

    /// <summary>Debug builds only: pretend the runtime is missing so the message can be seen on a PC that has it.</summary>
    public static bool Simulating { get; private set; }

    [Conditional("DEBUG")]
    public static void Simulate() => Simulating = true;

    public static EngineSource Source { get; } = LoadSource();

    /// <summary>The installed runtime's version, or null when it is missing (or simulated missing).</summary>
    public static string? Version()
    {
        if (Simulating) return null;
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString(Source.BrowserFolder);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"webview2 check failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>One cheap log line at app start; it never starts the engine.</summary>
    public static void LogStartup()
    {
        var version = Version();
        Log.Info($"webview2 present={(version is null ? "no" : "yes")} version={version ?? "none"}");
    }

    private static EngineSource LoadSource()
    {
        try
        {
#if DEBUG
            var source = EngineSource.FromSetting(Environment.GetEnvironmentVariable(FolderVariable));
#else
            var source = EngineSource.Shared; // the folder variable is a developer switch: Release ignores it
#endif
            if (source.BrowserFolder is { } folder && new DriveInfo(Path.GetPathRoot(folder)!).DriveType == DriveType.Network)
                throw new ArgumentException("network drive");
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            Log.Warn("webview2 engine folder refused; using the shared runtime");
            return EngineSource.Shared;
        }
    }
}
