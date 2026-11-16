using System.Runtime.InteropServices;
using Phrasor.App.Native;

namespace Phrasor.App.UI.Main;

/// <summary>
/// The main window's frame: Windows still sees a normal resizable window (so snap, Win+arrows, Alt+Space and the
/// shadow work), but the client area covers the title bar and <see cref="CaptionStrip"/> paints it instead.
/// The body hosts <see cref="WebHost"/>; closing the window disposes it.
/// </summary>
internal sealed class MainForm : Form
{
    private const int MinWidth = 880, MinHeight = 600, StartWidth = 1100, StartHeight = 720;

    private readonly CaptionStrip _strip = new();
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly WebHost _web = new() { Dock = DockStyle.Fill };

    public MainForm(AppServices services, Action openClassicSettings)
    {
        _web.ClassicSettingsRequested += openClassicSettings;
        _web.NotNowRequested += Close;
        _web.CoreReady += new MainBridge(this, services).Attach;
        Text = "Phrasor";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        _body.Controls.Add(_web);
        Controls.Add(_body);
        Controls.Add(_strip); // added last so it docks above the body
        ApplyColors();
        Theme.Changed += OnThemeChanged;
    }

    private void ApplyColors()
    {
        BackColor = _body.BackColor = Theme.Color("window");
        _web.ApplyTheme();
        _strip.Invalidate();
    }

    // Theme.Changed can arrive on another thread.
    private void OnThemeChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired && IsHandleCreated) BeginInvoke(ApplyColors);
        else if (!InvokeRequired) ApplyColors();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    // Sizes are in 96-dpi units; the form only knows its monitor's dpi once it has a handle. Later dpi changes are scaled by WinForms.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MinimumSize = new Size(LogicalToDeviceUnits(MinWidth), LogicalToDeviceUnits(MinHeight));
        ClientSize = new Size(LogicalToDeviceUnits(StartWidth), LogicalToDeviceUnits(StartHeight));
        CenterToScreen();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await _web.StartAsync(); // reports its own failures in the window
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _strip.Maximised = WindowState == FormWindowState.Maximized;
    }

    private int FrameThickness => Win32.GetSystemMetricsForDpi(Win32.SM_CYSIZEFRAME, (uint)DeviceDpi)
                                  + Win32.GetSystemMetricsForDpi(Win32.SM_CXPADDEDBORDER, (uint)DeviceDpi);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Win32.WM_NCCALCSIZE when m.WParam != 0:
                // Let Windows compute the client area, then give the title bar back to the client. A maximised
                // window overhangs the screen by the frame width, so the top is pulled in by that much.
                var top = Marshal.ReadInt32(m.LParam, sizeof(int));
                base.WndProc(ref m);
                Marshal.WriteInt32(m.LParam, sizeof(int), top + (Win32.IsZoomed(Handle) ? FrameThickness : 0));
                return;
            case Win32.WM_NCHITTEST:
                base.WndProc(ref m);
                m.Result = HitTest(m);
                return;
            case Win32.WM_NCMOUSEMOVE:
                _strip.Hot = IsCaptionButton((int)m.WParam) ? (int)m.WParam : 0;
                var track = new Win32.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE | Win32.TME_NONCLIENT, hwndTrack = Handle };
                Win32.TrackMouseEvent(ref track);
                break;
            case Win32.WM_NCMOUSELEAVE:
                _strip.Hot = 0;
                _strip.Pressed = 0;
                break;
            case Win32.WM_NCLBUTTONDOWN or Win32.WM_NCLBUTTONDBLCLK when IsCaptionButton((int)m.WParam):
                _strip.Pressed = (int)m.WParam; // the action runs on release, over the same button
                return;
            case Win32.WM_NCLBUTTONUP when IsCaptionButton((int)m.WParam):
                var pressed = _strip.Pressed;
                _strip.Pressed = 0;
                if (pressed == (int)m.WParam) RunCaptionButton(pressed);
                return;
            case Win32.WM_NCLBUTTONUP:
                _strip.Pressed = 0;
                break;
        }
        base.WndProc(ref m);
    }

    private IntPtr HitTest(Message m)
    {
        var result = (int)m.Result;
        var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));

        // The top edge sits inside the client area now, so it needs its own resize band.
        var band = FrameThickness;
        if (WindowState != FormWindowState.Maximized && result is Win32.HTCLIENT or Win32.HTLEFT or Win32.HTRIGHT or Win32.HTTOP
            && screen.Y - Bounds.Top < band)
        {
            if (screen.X - Bounds.Left < band) return Win32.HTTOPLEFT;
            if (Bounds.Right - screen.X <= band) return Win32.HTTOPRIGHT;
            return Win32.HTTOP;
        }

        if (result != Win32.HTCLIENT) return result;
        var client = PointToClient(screen);
        return client.Y < _strip.StripHeight ? _strip.HitTest(client) : Win32.HTCLIENT;
    }

    private static bool IsCaptionButton(int hit) => hit is Win32.HTMINBUTTON or Win32.HTMAXBUTTON or Win32.HTCLOSE;

    private void RunCaptionButton(int hit)
    {
        switch (hit)
        {
            case Win32.HTMINBUTTON: WindowState = FormWindowState.Minimized; break;
            case Win32.HTMAXBUTTON: WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
            default: Close(); break;
        }
    }
}
