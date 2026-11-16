using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Phrasor.App.Infrastructure;
using Phrasor.App.TextAccess;
using Phrasor.Core.Bridge;
using Phrasor.Core.Home;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;

namespace Phrasor.App.UI.Main;

/// <summary>
/// Connects the page to <see cref="BridgeDispatcher"/> (which does all the checking) and carries out what the page may ask for.
/// Only the top-level page's messages reach it: frames have their own event, which is never subscribed.
/// Logs through <see cref="BridgeLog"/> only. There is no key, text, clipboard or window-handle access here by design.
/// </summary>
internal sealed class MainBridge : IMainWindowApi
{
    private readonly MainForm _form;
    private readonly AppServices _services;
    private readonly MainHooks _hooks;
    private readonly BridgeDispatcher _dispatcher;
    private CoreWebView2? _core;
    private CancellationTokenSource? _run; // set while a Try-it run is going: only one at a time
    private int _runNumber;
    private CancellationTokenSource? _test; // set while Test connection is going: only one at a time
    private int _testNumber;
    private bool _recording; // the page is recording a chord, so the global shortcuts are released
    private string? _lastRewrite; // the one string clipboard.copy can copy; kept in memory only, dropped with the window

    public MainBridge(MainForm form, AppServices services, MainHooks hooks)
    {
        _form = form;
        _services = services;
        _hooks = hooks;
        _dispatcher = new BridgeDispatcher(this, Log.Info);
        form.FormClosed += (_, _) => { _run?.Cancel(); _test?.Cancel(); if (_recording) SetShortcutRecording(false); };
        // Leaving the window while recording (Alt+Tab, a click elsewhere) must not leave the shortcuts released.
        form.Deactivate += (_, _) => { if (_recording) SetShortcutRecording(false); };
    }

    public void Attach(CoreWebView2 core)
    {
        _core = core;
        core.WebMessageReceived += OnMessage;
#if DEBUG
        core.NavigationCompleted += (_, e) => { if (e.IsSuccess) DebugSelfTest(core); };
#endif
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { message = null; } // the page posted something that is not a string
        Post(_dispatcher.Handle(e.Source, isTopFrame: true, message));
    }

    private void Post(string? json)
    {
        if (json is null || _core is null) return;
        try { _core.PostWebMessageAsString(json); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window closed while the message was being handled.
        }
    }

    public HomeStatus GetHome() => HomeStatus.Build(_services.Settings, _services.Secrets, _hooks.IsPaused(), _hooks.RegisteredShortcuts());

    public bool SetPaused(bool paused)
    {
        _hooks.SetPaused(paused);
        return _hooks.IsPaused();
    }

    public int? StartTryIt(TryItRequest request)
    {
        if (_run is not null) return null;
        _lastRewrite = null;
        _run = new CancellationTokenSource();
        var number = ++_runNumber;
        _ = RunTryItAsync(number, request, _run);
        return number;
    }

    public void CancelTryIt() => _run?.Cancel();

    public bool CopyRewrite()
    {
        if (_lastRewrite is not { } text) return false;
        try { ClipboardText.Write(text); return true; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            Log.Warn($"tryit copy failed type={ex.GetType().Name}");
            return false;
        }
    }

    private async Task RunTryItAsync(int number, TryItRequest request, CancellationTokenSource cts)
    {
        await Task.Yield(); // the "started" reply must reach the page before any result does
        var clock = Stopwatch.StartNew();
        string outcome, json;
        try
        {
            var r = await _hooks.Optimize(request.Text, request.Mode, request.Task, cts.Token);
            _lastRewrite = r.Prompt;
            outcome = BridgeLog.Ok;
            json = BridgeEvents.TryItDone(number, new TryItOutput(r.Prompt, r.Verdict.ToString(), r.TaskType.ToString(), r.Changes, r.Warnings));
        }
        catch (Exception ex)
        {
            outcome = TryItErrors.KindOf(ex); // a kind from a fixed list; the message is never read
            json = BridgeEvents.TryItFailed(number, outcome);
        }
        cts.Dispose();
        _run = null;
        Log.Info(TryItLog.Describe(outcome, request.Text.Length, clock.ElapsedMilliseconds));
        Post(json);
    }

    public SettingsView GetSettings() => SettingsPage.BuildView(_services.Settings, _services.Secrets, StartupRegistration.IsEnabled());

    public SaveOutcome SaveSettings(SettingsDraft draft)
    {
        // Shortcut problems come first and stop the save: nothing is written, so the page can fix the chord and try again.
        var problems = SettingsPage.CheckShortcuts(draft.Shortcuts, draft.Voice.Enabled, _hooks.ProbeShortcut);
        if (problems.Count > 0) return new SaveOutcome(false, GetSettings(), problems, []);

        // Check and apply on a copy; the live settings change only after keys and file are written.
        // A throw here reaches the page as the bridge's plain "failed" reply (no text), and the live settings stay as they were.
        var next = SettingsStore.Clone(_services.Settings);
        SettingsPage.Apply(next, draft);
        foreach (var (id, key) in draft.NewKeys) _services.Secrets.Set(id, key);
        foreach (var id in draft.RemoveKeys) _services.Secrets.Set(id, null);
        SettingsStore.Save(AppPaths.SettingsFile, next);
        _services.Settings = next;
        try { StartupRegistration.Set(draft.StartWithWindows); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"startup registration failed type={ex.GetType().Name}");
        }
        Theme.Apply(next.Ui.Theme);
        // Recording ends with the save: the shortcuts come back, now with the new chords.
        _recording = false;
        _hooks.ApplyShortcuts();
        Log.Info($"settings saved from main window keysSet={draft.NewKeys.Count} keysRemoved={draft.RemoveKeys.Count}");
        return new SaveOutcome(true, GetSettings(), [], ShortcutWarnings(next));
    }

    private IReadOnlyList<ShortcutNote> ShortcutWarnings(AppSettings s)
    {
        var notes = new List<ShortcutNote>();
        void Check(string id, string chord, bool on)
        {
            if (on && !string.IsNullOrWhiteSpace(chord) && _hooks.ShortcutWarning(chord) is { } warning) notes.Add(new ShortcutNote(id, warning));
        }
        Check(HomeStatus.OptimizeId, s.Hotkeys.Optimize, true);
        Check(HomeStatus.SelectionId, s.Hotkeys.OptimizeSelection, true);
        Check(HomeStatus.VoiceId, s.Hotkeys.Voice, s.Voice.Enabled);
        return notes;
    }

    public void SetShortcutRecording(bool recording)
    {
        _recording = recording;
        _hooks.SetRecording(recording);
    }

    public int? StartConnectionTest(SettingsDraft draft)
    {
        if (_test is not null) return null;
        var settings = SettingsStore.Clone(_services.Settings);
        SettingsPage.Apply(settings, draft);
        var secrets = new OverlaySecretStore(_services.Secrets, draft.NewKeys);
        _test = new CancellationTokenSource();
        var number = ++_testNumber;
        _ = RunTestAsync(number, settings, secrets, _test);
        return number;
    }

    private async Task RunTestAsync(int number, AppSettings settings, ISecretStore secrets, CancellationTokenSource cts)
    {
        await Task.Yield(); // the "started" reply must reach the page before the result does
        var result = await SettingsPage.TestAsync(_services.Client, settings, secrets, cts.Token);
        cts.Dispose();
        _test = null;
        Log.Info($"settings test outcome={result.Error ?? BridgeLog.Ok} ms={result.Milliseconds}");
        Post(result.Ok ? BridgeEvents.SettingsTestDone(number, result) : BridgeEvents.SettingsTestFailed(number, result.Error!));
    }

    public MainWindowState GetState() =>
        new(_services.Settings.Ui.Theme, Theme.IsDark, _form.WindowState == FormWindowState.Maximized);

    public void SetTheme(ThemeMode mode)
    {
        _services.Settings.Ui.Theme = mode;
        _services.SaveSettings();
        Theme.Apply(mode);
    }

    // Run after the message handler returns, so the reply can still be posted before the window changes or closes.
    public void Window(WindowAction action) => _form.BeginInvoke(() =>
    {
        switch (action)
        {
            case WindowAction.Minimize: _form.WindowState = FormWindowState.Minimized; break;
            case WindowAction.ToggleMaximize:
                _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                break;
            default: _form.Close(); break;
        }
    });

    public void OpenLink(Uri url)
    {
        if (!LinkAllowList.IsAllowed(url)) return; // second check; the dispatcher only passes listed links
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log.Warn($"bridge link open failed type={ex.GetType().Name}"); }
    }

#if DEBUG
    // Debug builds only: set PHRASOR_BRIDGE_SELFTEST=1 to have the page exercise the bridge once it has loaded.
    // The results are the "bridge cmd=... outcome=..." lines in the log. The theme is put back afterwards.
    private static void DebugSelfTest(CoreWebView2 core)
    {
        if (Environment.GetEnvironmentVariable("PHRASOR_BRIDGE_SELFTEST") != "1") return;
        _ = core.ExecuteScriptAsync("""
            (async () => {
              const s = await phrasor.getState();
              await phrasor.setTheme('light');
              await phrasor.setTheme(s.theme);
              await phrasor.openLink('not-listed').catch(() => {});
              window.chrome.webview.postMessage('{"id":"x1","type":"nope"}');
              window.chrome.webview.postMessage('{broken');
              window.chrome.webview.postMessage(JSON.stringify({ id: 'x2', type: 'theme.set', payload: { mode: 'x'.repeat(5000) } }));
              for (let i = 0; i < 40; i++) window.chrome.webview.postMessage('{"id":"f' + i + '","type":"state.get"}');
            })();
            """);
    }
#endif
}
