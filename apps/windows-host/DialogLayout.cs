using System.Drawing;

namespace VolturaAir.Host;

internal static class DialogLayout
{
    public static Padding RootPadding => new(34, 28, 34, 28);

    public static TableLayoutPanel CreateRoot(int rowCount)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = RootPadding,
            RowCount = rowCount,
            ColumnCount = 1
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return root;
    }

    public static TableLayoutPanel CreateHeader(Label titleLabel, Label subtitleLabel, string title, string subtitle, int bottomMargin)
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, bottomMargin)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        titleLabel.Text = title;
        titleLabel.AutoSize = true;
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
        titleLabel.Margin = Padding.Empty;
        titleLabel.TextAlign = ContentAlignment.TopLeft;

        subtitleLabel.Text = subtitle;
        subtitleLabel.AutoSize = true;
        subtitleLabel.Dock = DockStyle.Top;
        subtitleLabel.Font = new Font("Segoe UI", 9.5f);
        subtitleLabel.Margin = new Padding(0, 6, 0, 0);
        subtitleLabel.TextAlign = ContentAlignment.TopLeft;

        header.SizeChanged += (_, _) =>
        {
            var maxTextWidth = Math.Max(1, header.ClientSize.Width);
            titleLabel.MaximumSize = new Size(maxTextWidth, 0);
            subtitleLabel.MaximumSize = new Size(maxTextWidth, 0);
        };

        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(subtitleLabel, 0, 1);
        return header;
    }
}
