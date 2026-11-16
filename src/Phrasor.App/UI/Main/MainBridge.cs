using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Phrasor.Core.Bridge;
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
    private readonly BridgeDispatcher _dispatcher;
    private CoreWebView2? _core;

    public MainBridge(MainForm form, AppServices services)
    {
        _form = form;
        _services = services;
        _dispatcher = new BridgeDispatcher(this, Log.Info);
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
        var reply = _dispatcher.Handle(e.Source, isTopFrame: true, message);
        if (reply is null || _core is null) return;
        try { _core.PostWebMessageAsString(reply); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window closed while the message was being handled.
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
