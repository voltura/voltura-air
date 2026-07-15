using System.Drawing;
using System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class ThemedToolStripRenderer(ThemePalette theme) : ToolStripProfessionalRenderer
{
    private static readonly TextFormatFlags MenuTextFormat =
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix |
        TextFormatFlags.PreserveGraphicsClipping;

    private readonly ThemePalette _theme = theme;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_theme.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_theme.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Border);
        var border = e.AffectedBounds;
        border.Width -= 1;
        border.Height -= 1;
        e.Graphics.DrawRectangle(pen, border);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var color = e.Item.Selected ? _theme.SurfaceRaised : _theme.Surface;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripSeparator)
        {
            base.OnRenderItemText(e);
            return;
        }

        var padding = e.Item.Padding;
        var checkGutter = HasCheckedItem(e.ToolStrip) ? Scale(e.Graphics, 28) : 0;
        var textBounds = new Rectangle(
            padding.Left + checkGutter,
            0,
            Math.Max(0, e.Item.Width - padding.Horizontal - checkGutter),
            e.Item.Height);

        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, e.TextColor, MenuTextFormat);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { Checked: true })
        {
            return;
        }

        var scale = e.Graphics.DpiX / 96f;
        var left = (int)Math.Round(8 * scale);
        var centerY = e.Item.Height / 2;
        using var pen = new Pen(_theme.Text, Math.Max(2f, 2f * scale))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        e.Graphics.DrawLines(pen,
        [
            new Point(left, centerY),
            new Point(left + (int)Math.Round(4 * scale), centerY + (int)Math.Round(4 * scale)),
            new Point(left + (int)Math.Round(12 * scale), centerY - (int)Math.Round(5 * scale))
        ]);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Border);
        var padding = e.ToolStrip?.Padding ?? Padding.Empty;
        var checkGutter = HasCheckedItem(e.ToolStrip) ? Scale(e.Graphics, 28) : 0;
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, padding.Left + checkGutter, y, e.Item.Width - padding.Right, y);
    }

    private static bool HasCheckedItem(ToolStrip? toolStrip) =>
        toolStrip?.Items.OfType<ToolStripMenuItem>().Any(item => item.Checked) == true;

    private static int Scale(Graphics graphics, int value) =>
        (int)Math.Round(value * graphics.DpiX / 96f);
}
