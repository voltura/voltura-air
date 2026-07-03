using System.ComponentModel;
using System.Drawing;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed class TechnicalDetailsForm : Form
{
    private readonly List<Control> _themedControls = new();
    private readonly TableLayoutPanel _detailsLayout = new();
    private readonly Icon _appIcon;
    private readonly Button _copyAllButton = new();
    private readonly Button _closeButton = new();
    private Panel? _detailsPanel;
    private ThemePalette _theme;
    private IReadOnlyList<TechnicalDetail> _details = Array.Empty<TechnicalDetail>();

    public TechnicalDetailsForm(Icon appIcon)
    {
        _theme = WindowsTheme.Current();
        _appIcon = appIcon;

        Text = "Voltura Air technical details";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 1140);
        Size = new Size(1080, 1180);

        BuildLayout();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        FormClosing += OnFormClosing;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Hide instead of closing to allow reopening from the menu
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ShowDetails(IWin32Window owner, IReadOnlyList<TechnicalDetail> details)
    {
        _details = details;
        PopulateDetails(details);
        Show(owner);
        WindowState = FormWindowState.Normal;
        ActiveControl = _closeButton;
        _closeButton.Focus();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            AppThemeSettings.Changed -= OnAppThemeChanged;
            FormClosing -= OnFormClosing;
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(ScaleLogical(24), ScaleLogical(22), ScaleLogical(24), ScaleLogical(20)),
            RowCount = 2,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(CommandButtonStyle.ActionRowHeight)));

        _detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false
        };

        _detailsLayout.Dock = DockStyle.Top;
        _detailsLayout.AutoSize = true;
        _detailsLayout.ColumnCount = 3;
        _detailsLayout.RowCount = 0;
        _detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(140)));
        _detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(60)));
        _detailsPanel.Controls.Add(_detailsLayout);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = new Padding(0, ScaleLogical(CommandButtonStyle.ActionTopPadding), 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        _closeButton.Text = "Close";
        _closeButton.Dock = DockStyle.Fill;
        _closeButton.Margin = Padding.Empty;
        CommandButtonStyle.Configure(_closeButton);
        _closeButton.Click += (_, _) => Hide();

        _copyAllButton.Text = "Copy all";
        _copyAllButton.Dock = DockStyle.Fill;
        _copyAllButton.Margin = new Padding(0, 0, ScaleLogical(CommandButtonStyle.ButtonGap), 0);
        CommandButtonStyle.Configure(_copyAllButton);
        _copyAllButton.Click += (_, _) => Clipboard.SetText(string.Join(Environment.NewLine, _details.Select(detail => $"{detail.Name}: {detail.Value}")));

        buttons.Controls.Add(_copyAllButton, 0, 0);
        buttons.Controls.Add(_closeButton, 1, 0);
        root.Controls.Add(_detailsPanel, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
    }

    private void PopulateDetails(IReadOnlyList<TechnicalDetail> details)
    {
        _detailsLayout.SuspendLayout();
        _detailsLayout.Controls.Clear();
        _detailsLayout.RowStyles.Clear();
        _detailsLayout.RowCount = 0;
        _themedControls.Clear();

        foreach (var detail in details)
        {
            AddDetailRow(detail);
        }

        _detailsLayout.ResumeLayout();
        ApplyTheme();
    }

    private void AddDetailRow(TechnicalDetail detail)
    {
        var row = _detailsLayout.RowCount++;
        var rowHeight = ScaleLogical(52);
        _detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));

        var nameLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = detail.Name,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Padding = new Padding(0, ScaleLogical(9), ScaleLogical(12), 0),
            Margin = new Padding(0, 0, 0, ScaleLogical(8))
        };

        var valueBox = new DetailValueBox
        {
            Dock = DockStyle.Fill,
            Value = detail.Value,
            Margin = new Padding(0, 0, 0, ScaleLogical(8)),
            MinimumSize = new Size(0, rowHeight - ScaleLogical(8))
        };

        var copyButton = new CopyIconButton
        {
            Dock = DockStyle.Top,
            Size = new Size(ScaleLogical(46), ScaleLogical(46)),
            Margin = new Padding(ScaleLogical(10), 0, 0, ScaleLogical(8))
        };
        copyButton.Click += (_, _) => Clipboard.SetText(detail.Value);

        _detailsLayout.Controls.Add(nameLabel, 0, row);
        _detailsLayout.Controls.Add(valueBox, 1, row);
        _detailsLayout.Controls.Add(copyButton, 2, row);
        _themedControls.Add(nameLabel);
        _themedControls.Add(valueBox);
        _themedControls.Add(copyButton);
    }

    private void ApplyTheme()
    {
        _theme = WindowsTheme.Current();
        WindowsTheme.ApplyImmersiveDarkMode(this, _theme.IsDark);

        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        if (_detailsPanel is not null)
        {
            _detailsPanel.BackColor = _theme.Window;
        }

        StyleCommandButton(_closeButton, primary: false);
        StyleCommandButton(_copyAllButton, primary: true);

        foreach (var control in _themedControls)
        {
            switch (control)
            {
                case Label label when label.Font.Bold:
                    label.ForeColor = _theme.Text;
                    label.BackColor = _theme.Window;
                    break;
                case Label label:
                    label.ForeColor = _theme.Text;
                    label.BackColor = _theme.Surface;
                    break;
                case DetailValueBox valueBox:
                    valueBox.ApplyTheme(_theme);
                    break;
                case CopyIconButton button:
                    button.ApplyTheme(_theme);
                    break;
            }
        }
    }

    private void StyleCommandButton(Button button, bool primary)
    {
        CommandButtonStyle.ApplyTheme(button, _theme, primary ? CommandButtonKind.Primary : CommandButtonKind.Normal);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            BeginInvoke(ApplyTheme);
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(ApplyTheme);
            return;
        }

        ApplyTheme();
    }

    private int ScaleLogical(int value)
    {
        return LogicalToDeviceUnits(value);
    }

    private sealed class DetailValueBox : UserControl
    {
        private readonly TextBox _textBox = new();
        private ThemePalette? _theme;

        public DetailValueBox()
        {
            Padding = new Padding(10, 8, 10, 6);
            DoubleBuffered = true;
            TabStop = false;

            _textBox.BorderStyle = BorderStyle.None;
            _textBox.Font = new Font("Cascadia Mono", 8.25f);
            _textBox.ReadOnly = true;
            _textBox.TabStop = false;
            _textBox.HideSelection = true;
            _textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _textBox.GotFocus += (_, _) => ClearSelection();
            _textBox.MouseUp += (_, _) => ClearSelection();

            Controls.Add(_textBox);
            Resize += (_, _) => LayoutTextBox();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Value
        {
            get => _textBox.Text;
            set
            {
                _textBox.Text = value;
                ClearSelection();
            }
        }

        public void ApplyTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = theme.Surface;
            _textBox.BackColor = theme.Surface;
            _textBox.ForeColor = theme.Text;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutTextBox();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var pen = new Pen(_theme?.Border ?? SystemColors.ControlDark);
            var border = ClientRectangle;
            border.Width -= 1;
            border.Height -= 1;
            e.Graphics.DrawRectangle(pen, border);
        }

        private void ClearSelection()
        {
            _textBox.SelectionStart = 0;
            _textBox.SelectionLength = 0;
        }

        private void LayoutTextBox()
        {
            var width = Math.Max(1, ClientSize.Width - Padding.Left - Padding.Right);
            var top = Math.Max(Padding.Top, (ClientSize.Height - _textBox.Height) / 2);
            _textBox.SetBounds(Padding.Left, top, width, _textBox.Height);
        }
    }

    private sealed class CopyIconButton : Button
    {
        private ThemePalette? _theme;

        public CopyIconButton()
        {
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            AccessibleName = "Copy value";
        }

        public void ApplyTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = theme.SurfaceRaised;
            ForeColor = theme.Accent;
            FlatAppearance.BorderColor = theme.Border;
            FlatAppearance.MouseOverBackColor = ControlPaint.Light(theme.SurfaceRaised);
            FlatAppearance.MouseDownBackColor = ControlPaint.Dark(theme.SurfaceRaised);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var color = _theme?.Accent ?? ForeColor;
            using var pen = new Pen(color, 1.8f);
            pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Center;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var size = Math.Min(ClientSize.Width, ClientSize.Height);
            var icon = new Rectangle(
                (ClientSize.Width - size) / 2 + size / 4,
                (ClientSize.Height - size) / 2 + size / 4,
                size / 2,
                size / 2);
            var back = icon;
            back.Offset(-5, -5);

            e.Graphics.DrawRectangle(pen, back);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(brush, icon);
            e.Graphics.DrawRectangle(pen, icon);
        }
    }
}

public sealed record TechnicalDetail(string Name, string Value);
