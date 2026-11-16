using System.ComponentModel;
using System.Drawing.Drawing2D;
using Phrasor.App.Native;

namespace Phrasor.App.UI.Main;

/// <summary>
/// The painted title bar of <see cref="MainForm"/>: title text and the three caption buttons.
/// It never handles the mouse itself. It answers "transparent" to hit tests so the form gets them, which keeps
/// dragging, double-click and the Windows 11 snap-layout flyout working. The form tells it which button is hot.
/// </summary>
internal sealed class CaptionStrip : Control
{
    private const int LogicalHeight = 32, ButtonWidth = 46, GlyphSize = 10, TitlePad = 12;
    private const int HoverAlpha = 28, PressedAlpha = 56;

    // Close hover is red in both themes, as in the prototype; it is not a palette token.
    private static readonly Color CloseHover = Color.FromArgb(0xC4, 0x2B, 0x1C);

    private int _hot, _pressed;
    private bool _maximised;

    public CaptionStrip()
    {
        Dock = DockStyle.Top;
        // ResizeRedraw: the buttons are right-aligned, so after a resize only the newly exposed strip is repainted
        // and the old buttons stay behind (a ghost set mid-strip, and the real close button half drawn).
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Height = Scale(LogicalHeight);
    }

    public int StripHeight => Scale(LogicalHeight);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Maximised
    {
        get => _maximised;
        set { if (_maximised != value) { _maximised = value; Invalidate(); } }
    }

    /// <summary>The caption button under the mouse (HTMINBUTTON, HTMAXBUTTON, HTCLOSE) or 0.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Hot
    {
        get => _hot;
        set { if (_hot != value) { _hot = value; Invalidate(); } }
    }

    /// <summary>The caption button being held down, or 0.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Pressed
    {
        get => _pressed;
        set { if (_pressed != value) { _pressed = value; Invalidate(); } }
    }

    /// <summary>Hit-test code for a point in this strip's client coordinates.</summary>
    public int HitTest(Point p)
    {
        var w = Scale(ButtonWidth);
        if (p.X >= Width - w) return Win32.HTCLOSE;
        if (p.X >= Width - 2 * w) return Win32.HTMAXBUTTON;
        if (p.X >= Width - 3 * w) return Win32.HTMINBUTTON;
        return Win32.HTCAPTION;
    }

    private int Scale(int logical) => (int)Math.Round(logical * (DeviceDpi / 96.0));

    protected override void WndProc(ref Message m)
    {
        // Let the form decide: it owns drag, resize and the caption buttons.
        if (m.Msg == Win32.WM_NCHITTEST) { m.Result = Win32.HTTRANSPARENT; return; }
        base.WndProc(ref m);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Scale(LogicalHeight);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Color("window"));
        var text = Theme.Color("text");
        var w = Scale(ButtonWidth);

        TextRenderer.DrawText(g, "Phrasor", Font, new Rectangle(Scale(TitlePad), 0, Width - 3 * w - Scale(TitlePad), Height),
            Theme.Color("muted"), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var buttons = new[] { Win32.HTMINBUTTON, Win32.HTMAXBUTTON, Win32.HTCLOSE };
        for (var i = 0; i < buttons.Length; i++)
        {
            var code = buttons[i];
            var rect = new Rectangle(Width - (3 - i) * w, 0, w, Height);
            var glyph = text;
            if (_hot == code && (_pressed == 0 || _pressed == code))
            {
                if (code == Win32.HTCLOSE)
                {
                    using var red = new SolidBrush(_pressed == code ? Color.FromArgb(0xB0, CloseHover) : CloseHover);
                    g.FillRectangle(red, rect);
                    glyph = Color.White;
                }
                else
                {
                    using var shade = new SolidBrush(Color.FromArgb(_pressed == code ? PressedAlpha : HoverAlpha, text));
                    g.FillRectangle(shade, rect);
                }
            }
            DrawGlyph(g, code, rect, glyph);
        }
    }

    private void DrawGlyph(Graphics g, int code, Rectangle rect, Color color)
    {
        var size = Scale(GlyphSize);
        var x = rect.Left + (rect.Width - size) / 2;
        var y = rect.Top + (rect.Height - size) / 2;
        using var pen = new Pen(color, Math.Max(1, DeviceDpi / 96));
        g.SmoothingMode = code == Win32.HTCLOSE ? SmoothingMode.AntiAlias : SmoothingMode.None;
        switch (code)
        {
            case Win32.HTMINBUTTON:
                g.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
                break;
            case Win32.HTMAXBUTTON when _maximised:
                var inset = Math.Max(2, size / 5);
                g.DrawRectangle(pen, x, y + inset, size - inset, size - inset);
                g.DrawLines(pen, [new Point(x + inset, y + inset), new Point(x + inset, y), new Point(x + size, y), new Point(x + size, y + size - inset), new Point(x + size - inset, y + size - inset)]);
                break;
            case Win32.HTMAXBUTTON:
                g.DrawRectangle(pen, x, y, size, size);
                break;
            default:
                g.DrawLine(pen, x, y, x + size, y + size);
                g.DrawLine(pen, x + size, y, x, y + size);
                break;
        }
    }
}
