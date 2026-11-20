using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Phrasor.App.Infrastructure;
using Phrasor.Core.Web;

namespace Phrasor.App.UI.Main;

/// <summary>
/// The main window's browser. It loads only <see cref="EmbeddedWeb"/> files under one made-up host, refuses every other
/// navigation and network request, keeps no profile data after the window closes, and turns the browser's own features off.
/// If the browser cannot start, the window shows a plain message instead and nothing else is affected.
/// Logs events, timings and error types only; never page content.
/// </summary>
internal sealed class WebHost : Panel
{
    private static string FolderName => AppPaths.Root.WebFolderName;
    private const int DeleteAttempts = 10, DeleteRetryMs = 500;

    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly EngineMissingPanel _problem = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Stopwatch _clock = new();
    private string? _userDataFolder;

    /// <summary>Raised once the browser is locked down and before the first page loads; the bridge attaches here.</summary>
    public event Action<CoreWebView2>? CoreReady;

    /// <summary>Buttons on the "web view missing" message.</summary>
    public event Action? ClassicSettingsRequested, NotNowRequested;

    public WebHost()
    {
        Controls.Add(_problem);
        Controls.Add(_view);
        _problem.ClassicSettingsRequested += () => ClassicSettingsRequested?.Invoke();
        _problem.NotNowRequested += () => NotNowRequested?.Invoke();
        _problem.Recovered += () => { _problem.Visible = false; _ = StartAsync(); };
        ApplyTheme();
    }

    /// <summary>Removes folders left behind by an earlier run that crashed or was killed. Call before a window opens.</summary>
    public static void CleanLeftovers()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), FolderName);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"webview2 cleanup failed: {ex.GetType().Name}");
        }
    }

    public async Task StartAsync()
    {
        _clock.Restart();
        if (EngineCheck.Version() is null)
        {
            Log.Warn("webview2 runtime not found");
            ShowProblem(EngineProblem.Missing);
            return;
        }
        try
        {
            CleanLeftovers();
            _userDataFolder = Path.Combine(Path.GetTempPath(), FolderName, Guid.NewGuid().ToString("N"));
            var options = new CoreWebView2EnvironmentOptions
            {
                // Second layer after the request filter: every host except ours fails to resolve.
                AdditionalBrowserArguments = "--host-resolver-rules=\"MAP * ~NOTFOUND\" --disable-background-networking",
                // Keep crash reports on this PC.
                IsCustomCrashReportingEnabled = true,
            };
            var environment = await CoreWebView2Environment.CreateAsync(BrowserExecutableFolder(), _userDataFolder, options);
            if (IsDisposed) return;
            await _view.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            Lock(_view.CoreWebView2);
            CoreReady?.Invoke(_view.CoreWebView2);
            ApplyTheme();
            _view.Visible = true;
            Log.Info($"webview2 ready version={environment.BrowserVersionString} ms={_clock.ElapsedMilliseconds}");
            _view.CoreWebView2.Navigate(WebSecurityPolicy.StartUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Log.Warn("webview2 runtime not found");
            ShowProblem(EngineProblem.Missing);
        }
        catch (Exception ex)
        {
            Log.Error($"webview2 start failed: {ex.GetType().Name}");
            ShowProblem(EngineProblem.CouldNotStart);
        }
    }

    public void ApplyTheme()
    {
        BackColor = _view.DefaultBackgroundColor = Theme.Color("window");
        _problem.ApplyTheme();
        if (_view.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = Theme.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    // The one place that names an engine folder. Null means the shared Evergreen runtime.
    private static string? BrowserExecutableFolder() => EngineCheck.Source.BrowserFolder;

    private void ShowProblem(EngineProblem problem)
    {
        _view.Visible = false;
        _problem.Describe(problem);
        _problem.Visible = true;
    }

    private void Lock(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.IsScriptEnabled = true; // only files from our own origin run (CSP script-src 'self'); see MainBridge for what they may ask
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsReputationCheckingRequired = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
#endif

        core.NavigationStarting += (_, e) => e.Cancel = !WebSecurityPolicy.IsAllowedUrl(e.Uri);
        core.FrameNavigationStarting += (_, e) => e.Cancel = !WebSecurityPolicy.IsAllowedUrl(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ProcessFailed += (_, e) => Log.Warn($"webview2 process failed kind={e.ProcessFailedKind}");
        core.NavigationCompleted += (_, e) =>
        {
            if (!_clock.IsRunning) return;
            _clock.Stop();
            Log.Info($"webview2 first page ok={e.IsSuccess} ms={_clock.ElapsedMilliseconds}");
        };

        // Every request, any type: ours is answered from the exe, the rest gets 403 without touching the network.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var environment = _view.CoreWebView2.Environment;
        var path = e.Request.Method == "GET" ? WebSecurityPolicy.FilePathFor(e.Request.Uri) : null;
        var file = path is null ? null : EmbeddedWeb.Find(path);
        if (file is null)
        {
            // Not ours, or not a file we have. The URL is not logged: it could carry page data.
            Log.Info("webview2 request blocked");
            e.Response = environment.CreateWebResourceResponse(null, path is null ? 403 : 404, path is null ? "Forbidden" : "Not Found", "");
            return;
        }
        var headers = string.Join("\r\n", WebSecurityPolicy.ResponseHeaders(file.Value.ContentType).Select(h => $"{h.Name}: {h.Value}"));
        e.Response = environment.CreateWebResourceResponse(new MemoryStream(file.Value.Body), 200, "OK", headers);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.Dispose();
            DeleteUserDataFolder(_userDataFolder);
        }
        base.Dispose(disposing);
    }

    // The browser processes can hold the folder for a moment after the control is gone, so retry off the UI thread.
    // If the app exits first, CleanLeftovers removes it at the next start.
    private static void DeleteUserDataFolder(string? folder)
    {
        if (folder is null) return;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < DeleteAttempts; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(DeleteRetryMs);
                }
            }
            Log.Warn("webview2 user data folder not deleted");
        });
    }
}
