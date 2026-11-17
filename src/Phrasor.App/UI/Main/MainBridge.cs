using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Phrasor.App.Infrastructure;
using Phrasor.App.TextAccess;
using Phrasor.Core.Bridge;
using Phrasor.Core.Eval;
using Phrasor.Core.History;
using Phrasor.Core.Home;
using Phrasor.Core.Llm;
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
    private CancellationTokenSource? _quality; // set while a quality check is going: only one at a time
    private int _qualityNumber;
    private string? _casesPath; // the test set the user chose; the page only ever sees its file name and counts
    private IReadOnlyList<EvalCase>? _cases;
    private string? _resultsDir; // the last finished run's folder, for "Open results"
    private bool _recording; // the page is recording a chord, so the global shortcuts are released
    private string? _lastRewrite; // the one string clipboard.copy can copy; kept in memory only, dropped with the window
    private string? _lastOriginal; // with _lastRewrite, the only text history.save can store: the page never supplies any
    private bool _lastSaved;

    public MainBridge(MainForm form, AppServices services, MainHooks hooks)
    {
        _form = form;
        _services = services;
        _hooks = hooks;
        _dispatcher = new BridgeDispatcher(this, Log.Info);
        form.FormClosed += (_, _) => { _run?.Cancel(); _test?.Cancel(); _quality?.Cancel(); if (_recording) SetShortcutRecording(false); };
        // A check can take an hour and stopping it loses its results, so closing the window asks first.
        form.FormClosing += (_, e) =>
        {
            if (_quality is not null && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(form, "A quality check is running. Closing the window stops it and its results are lost. Close anyway?", "Phrasor",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                e.Cancel = true;
        };
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
        _lastRewrite = _lastOriginal = null;
        _lastSaved = false;
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
            _lastOriginal = request.Text;
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

    public HistoryListView ListHistory(string query, int limit) => Guarded(() =>
    {
        _services.PruneHistory();
        var store = _services.History;
        return HistoryView.Build(store.List(), store.LoadOutcome, _services.RetentionDays, query, limit, DateTime.UtcNow, TimeZoneInfo.Local);
    });

    public HistoryEntryView? GetHistory(string id) => Guarded(() => _services.History.Get(id) is { } e ? new HistoryEntryView(e.Id, e.Original, e.Rewrite) : null);

    public HistorySaveResult SaveHistory()
    {
        if (string.IsNullOrWhiteSpace(_lastRewrite) || _lastOriginal is null) return HistorySaveResult.NothingToSave;
        if (_lastSaved) return HistorySaveResult.AlreadySaved;
        Guarded(() => { _services.SaveToHistory(_lastOriginal, _lastRewrite); return true; });
        _lastSaved = true;
        return HistorySaveResult.Saved;
    }

    public bool CopyHistory(string id, bool original)
    {
        if (Guarded(() => _services.History.Get(id)) is not { } entry) return false;
        try { ClipboardText.Write(original ? entry.Original : entry.Rewrite); return true; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            Log.Warn($"history copy failed type={ex.GetType().Name}");
            return false;
        }
    }

    public bool DeleteHistory(string id) => Guarded(() => _services.History.Delete(id));

    public void DeleteAllHistory() => Guarded(() => { _services.History.Clear(); return true; });

    public void SetHistoryRetention(int days)
    {
        _services.Settings.History.RetentionDays = HistoryRetention.Normalise(days);
        _services.SaveSettings();
        Guarded(() => { _services.PruneHistory(); return true; });
    }

    // Any failure of the History file becomes one plain error for the page; only the exception type is logged.
    private T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (AppServices.IsHistoryFailure(ex))
        {
            Log.Warn($"history failed type={ex.GetType().Name}");
            throw new HistoryUnavailableException();
        }
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
        // A model that was removed, or went back to the provider's key, leaves no key behind.
        foreach (var orphan in ModelList.OrphanedKeyIds(_services.Settings, next)) _services.Secrets.Set(orphan, null);
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

    public int? StartFirstRunTest(FirstRunKey key)
    {
        if (_test is not null) return null;
        var settings = SettingsStore.Clone(_services.Settings);
        FirstRun.Apply(settings, key.Provider);
        var secrets = new OverlaySecretStore(_services.Secrets, new Dictionary<string, string> { [key.Provider] = key.Key });
        _test = new CancellationTokenSource();
        var number = ++_testNumber;
        _ = RunTestAsync(number, settings, secrets, _test);
        return number;
    }

    public void SaveFirstRunKey(FirstRunKey key)
    {
        // As in SaveSettings: check and apply on a copy, write the key and the file, then switch the live settings.
        var next = SettingsStore.Clone(_services.Settings);
        FirstRun.Apply(next, key.Provider);
        _services.Secrets.Set(key.Provider, key.Key);
        SettingsStore.Save(AppPaths.SettingsFile, next);
        _services.Settings = next;
        Log.Info($"first run key saved provider={key.Provider}");
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

    public QualityView GetQuality()
    {
        var judge = _services.Settings.Judge;
        var provider = FirstRun.Providers.Contains(judge.Provider) ? judge.Provider : ProviderPresets.Gemini.Id;
        return new QualityView(provider, judge.Model, !string.IsNullOrEmpty(_services.Secrets.Get(provider)),
            provider == _services.Settings.Primary.Provider, CasesView(), _quality is not null, _resultsDir is not null,
            FirstRun.Providers.ToDictionary(id => id, id => ProviderPresets.Get(id).DefaultModel));
    }

    private QualityCasesView? CasesView() => _cases is null || _casesPath is null ? null
        : new QualityCasesView(Path.GetFileName(_casesPath), _cases.Count, _cases.Count(c => c.HeldOut));

    public void SetQualityJudge(string provider, string model)
    {
        var next = SettingsStore.Clone(_services.Settings);
        next.Judge = new EndpointSettings { Provider = provider, Model = model };
        SettingsStore.Save(AppPaths.SettingsFile, next);
        _services.Settings = next;
        Log.Info($"quality judge saved provider={provider}");
    }

    public QualityCasesView? ChooseQualityCases()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the test set (eval\\cases.json)",
            Filter = "Test sets (*.json)|*.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(_form) != DialogResult.OK) return null;
        try
        {
            _cases = EvalRunner.LoadCases(dialog.FileName);
            _casesPath = dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Log.Warn($"quality test set refused type={ex.GetType().Name}");
            throw new QualityNotReadyException();
        }
        Log.Info($"quality test set chosen cases={_cases.Count}");
        return CasesView();
    }

    public QualityPlanView PlanQuality(QualityRunOptions options) =>
        _cases is null ? throw new QualityNotReadyException() : QualityPage.Plan(_cases, options);

    public int? StartQualityRun(QualityRunOptions options)
    {
        if (_quality is not null) return null;
        if (_cases is null || _casesPath is null) throw new QualityNotReadyException();
        try { EndpointResolver.Resolve(_services.Settings, _services.Secrets); }
        catch (MissingKeyException) { throw new QualityNotReadyException(); }
        _quality = new CancellationTokenSource();
        var number = ++_qualityNumber;
        _ = RunQualityAsync(number, _casesPath, QualityPage.Plan(_cases, options).Runs, options, _quality);
        return number;
    }

    public void CancelQualityRun() => _quality?.Cancel();

    private async Task RunQualityAsync(int number, string casesPath, int total, QualityRunOptions o, CancellationTokenSource cts)
    {
        await Task.Yield(); // the "started" reply must reach the page before any progress does
        var dir = Path.Combine(AppPaths.DataDir, "quality", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var progress = new QualityProgress();
        string state;
        (int Passed, int Errored, int Total) counts = (0, 0, 0);
        try
        {
            // Called on this (the UI) thread: the callback is posted through the context RunAsync was started on.
            var ok = await EvalCommand.RunAsync(_services, casesPath, dir, o.Judge, TimeSpan.FromSeconds(8), o.Repeat, null, o.IncludeHeldOut, cts.Token,
                line =>
                {
                    progress.Update(line);
                    Post(BridgeEvents.QualityProgress(number, Math.Min(progress.Current, total), total, progress.Waiting));
                });
            state = cts.IsCancellationRequested ? QualityState.Cancelled : !ok ? QualityState.Failed : progress.Stopped ? QualityState.Stopped : QualityState.Done;
            if (state is QualityState.Done or QualityState.Stopped)
            {
                counts = QualityPage.Summarise(await File.ReadAllTextAsync(Path.Combine(dir, "results.json")));
                _resultsDir = dir;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            state = QualityState.Failed;
        }
        cts.Dispose();
        _quality = null;
        // Counts and a state from a fixed list: no case text, rewrite or path reaches the log or the page.
        Log.Info($"quality check state={state} passed={counts.Passed} errored={counts.Errored} total={counts.Total}");
        Post(BridgeEvents.QualityFinished(number, state, counts.Passed, counts.Errored, counts.Total));
    }

    public bool OpenQualityResults()
    {
        if (_resultsDir is null || !Directory.Exists(_resultsDir)) return false;
        try { Process.Start(new ProcessStartInfo(_resultsDir) { UseShellExecute = true }); return true; }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            Log.Warn($"open quality results failed type={ex.GetType().Name}");
            return false;
        }
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

    public void OpenDataFolder(DataFolder folder)
    {
        var dir = folder == DataFolder.Logs ? AppPaths.LogDir : AppPaths.DataDir;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"open data folder failed type={ex.GetType().Name}");
        }
    }

    public bool DeleteAllData()
    {
        Log.Info("delete all data from main window"); // before the folder goes: logging recreates it
        var complete = true;
        try { _services.Secrets.Clear(); StartupRegistration.Set(false); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            complete = false;
            Log.Warn($"delete all data: keys or startup entry failed type={ex.GetType().Name}");
        }
        foreach (var dir in new[] { AppPaths.DataDir, AppPaths.HistoryDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                Log.Warn($"delete all data: a folder failed type={ex.GetType().Name}");
            }
        }
        if (!complete) return false;
        Log.Stop(); // otherwise the exit line would bring the logs folder back
        _form.BeginInvoke(Application.Exit); // after the reply is posted
        return true;
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
