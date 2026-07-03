using System.Drawing;
using System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
    private static readonly TextFormatFlags MenuTextFormat =
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix |
        TextFormatFlags.PreserveGraphicsClipping;

    private readonly ThemePalette _theme;

    public ThemedToolStripRenderer(ThemePalette theme)
    {
        _theme = theme;
    }

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
        var textBounds = new Rectangle(
            padding.Left,
            0,
            Math.Max(0, e.Item.Width - padding.Horizontal),
            e.Item.Height);

        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, e.TextColor, MenuTextFormat);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Border);
        var padding = e.ToolStrip?.Padding ?? Padding.Empty;
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, padding.Left, y, e.Item.Width - padding.Right, y);
    }
}
