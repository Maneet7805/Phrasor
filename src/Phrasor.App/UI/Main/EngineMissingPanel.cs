using System.Diagnostics;
using Phrasor.Core.Web;

namespace Phrasor.App.UI.Main;

internal enum EngineProblem { Missing, CouldNotStart }

/// <summary>
/// The plain message the main window shows instead of its pages when the browser engine is missing or will not start.
/// Four buttons; nothing is downloaded or run until "Install now" is clicked. The rest of Phrasor does not need this.
/// </summary>
internal sealed class EngineMissingPanel : Panel
{
    private const int ContentWidth = 520, BarHeight = 8, MarqueeMs = 30;
    private const string KeepsWorking = "Shortcuts, the review window and dictation keep working meanwhile.";

    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Margin = new Padding(0, 0, 0, 12) };
    private readonly Label _body = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 12), Visible = false };
    private readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = MarqueeMs, Size = new Size(ContentWidth, BarHeight), Visible = false, Margin = new Padding(0, 0, 0, 12) };
    private readonly Button _install = new() { Text = "Install now", AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) };
    private readonly Button _page = new() { Text = "Open Microsoft's page", AutoSize = true };
    private readonly Button _classic = new() { Text = "Use classic settings", AutoSize = true };
    private readonly Button _notNow = new() { Text = "Not now", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Visible = false };
    private readonly FlowLayoutPanel _buttons = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _column = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
    private CancellationTokenSource? _installCancel;

    /// <summary>The runtime is installed and found: the window should start its pages.</summary>
    public event Action? Recovered;
    public event Action? ClassicSettingsRequested;
    public event Action? NotNowRequested;

    public EngineMissingPanel()
    {
        foreach (var label in new[] { _title, _body, _status }) label.MaximumSize = new Size(ContentWidth, 0);
        foreach (var button in new[] { _install, _page, _classic, _notNow, _cancel }) button.Margin = new Padding(0, 0, 8, 8);
        _buttons.Controls.AddRange([_install, _page, _classic, _notNow, _cancel]);
        _column.Controls.AddRange([_title, _body, _status, _bar, _buttons]);
        Controls.Add(_column);
        _install.Click += async (_, _) => await InstallAsync();
        _page.Click += (_, _) => OpenManualPage();
        _classic.Click += (_, _) => ClassicSettingsRequested?.Invoke();
        _notNow.Click += (_, _) => NotNowRequested?.Invoke();
        _cancel.Click += (_, _) => _installCancel?.Cancel();
        ApplyTheme();
    }

    public void Describe(EngineProblem problem)
    {
        _title.Text = problem == EngineProblem.Missing
            ? "This window needs Microsoft's web view"
            : "This window couldn't start its web view";
        _body.Text = (problem == EngineProblem.Missing
            ? "It isn't installed on this PC, so this window can't open yet."
            : "It is installed but did not start. Installing it again from Microsoft may fix this.")
            + "\n\n\"Install now\" downloads Microsoft's small installer (about 2 MB) from Microsoft and installs it for your Windows account. No admin rights needed.\n\n"
            + KeepsWorking;
        _status.Visible = false;
        SetBusy(false);
        _install.Focus();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Color("window");
        ForeColor = Theme.Color("text");
        _status.ForeColor = Theme.Color("text");
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _column.Location = new Point(Math.Max(0, (ClientSize.Width - _column.Width) / 2), Math.Max(0, (ClientSize.Height - _column.Height) / 2));
    }

    private void SetBusy(bool busy)
    {
        _bar.Visible = _cancel.Visible = busy;
        _install.Enabled = _page.Enabled = _classic.Enabled = _notNow.Enabled = !busy;
        PerformLayout();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visible = true;
        PerformLayout();
    }

    private async Task InstallAsync()
    {
        using var cancel = _installCancel = new CancellationTokenSource();
        _status.Visible = false;
        SetBusy(true);
        var result = await RuntimeInstaller.RunAsync(dryRun: EngineCheck.Simulating, cancel.Token);
        _installCancel = null;
        if (IsDisposed) return;
        SetBusy(false);

        if (result.NotRun) SetStatus("Simulation: Microsoft's installer was downloaded and its signature checked, but it was not run.");
        else if (result.Outcome != InstallOutcome.Installed) SetStatus(result.Message);
        else if (EngineCheck.Version() is not null) Recovered?.Invoke();
        else SetStatus("Microsoft's installer finished, but Phrasor can't see the web view yet. Quit Phrasor from the tray icon and start it again.");
    }

    private void OpenManualPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RuntimeInstallPlan.ManualPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"webview2 manual page did not open: {ex.GetType().Name}");
            SetStatus("Couldn't open your browser. Search for \"WebView2 Runtime\" on Microsoft's site.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _installCancel?.Cancel(); // closing the window while installing stops the installer
        base.Dispose(disposing);
    }
}
