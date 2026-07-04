using System.Drawing.Drawing2D;

namespace VolturaAir.Host;

internal sealed class ThemedCheckBox : CheckBox
{
    private const int LogicalControlHeight = 34;
    private const int LogicalBoxSize = 22;

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix;

    private ThemePalette _theme = WindowsTheme.Current();
    private bool _isHovering;
    private bool _isPressing;

    public ThemedCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        AutoSize = false;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(0, ScaleLogical(LogicalControlHeight));
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font);
        var width = Padding.Left + ScaleLogical(LogicalBoxSize + 12) + textSize.Width + Padding.Right;
        return new Size(width, ScaleLogical(LogicalControlHeight));
    }

    public void ApplyTheme(ThemePalette theme)
    {
        _theme = theme;
        BackColor = theme.Window;
        ForeColor = theme.Text;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _isHovering = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _isHovering = false;
        _isPressing = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressing = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _isPressing = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var boxSize = ScaleLogical(LogicalBoxSize);
        var boxLeft = Padding.Left;
        var boxTop = (ClientSize.Height - boxSize) / 2;
        var boxBounds = new Rectangle(boxLeft, boxTop, boxSize, boxSize);
        var textLeft = boxBounds.Right + ScaleLogical(12);
        var textBounds = new Rectangle(
            textLeft,
            0,
            Math.Max(1, ClientSize.Width - textLeft - Padding.Right),
            ClientSize.Height);

        var borderColor = Checked ? _theme.Accent : _theme.Border;
        var boxFill = Checked
            ? (_isPressing ? ControlPaint.Dark(_theme.Accent) : _theme.Accent)
            : (_isHovering ? _theme.SurfaceRaised : _theme.Surface);

        using (var path = CreateRoundRect(boxBounds, ScaleLogical(6)))
        using (var fillBrush = new SolidBrush(boxFill))
        using (var borderPen = new Pen(borderColor, ScaleLogical(1)))
        {
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        if (Checked)
        {
            DrawCheckMark(e.Graphics, boxBounds);
        }

        var textColor = Enabled ? ForeColor : _theme.MutedText;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, TextFlags);
    }

    private void DrawCheckMark(Graphics graphics, Rectangle boxBounds)
    {
        using var pen = new Pen(_theme.AccentText, Math.Max(2f, ScaleLogical(2)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var points = new[]
        {
            new Point(boxBounds.Left + ScaleLogical(5), boxBounds.Top + ScaleLogical(11)),
            new Point(boxBounds.Left + ScaleLogical(9), boxBounds.Top + ScaleLogical(15)),
            new Point(boxBounds.Left + ScaleLogical(17), boxBounds.Top + ScaleLogical(7))
        };
        graphics.DrawLines(pen, points);
    }

    private static GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private int ScaleLogical(int value)
    {
        return (int)Math.Round(value * DeviceDpi / 96f);
    }
}
