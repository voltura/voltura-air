using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal sealed class ThemedWindowChrome : NativeWindow, IDisposable
{
    private const int TitleBarHeight = 48;
    private const int BorderThickness = 1;
    private const int ResizeGripThickness = 8;
    private const int CaptionButtonWidth = 48;
    private const int IconSize = 18;
    private const int DeviceManagerHeightCompensation = 180;

    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int HtCaption = 2;

    private readonly Form _form;
    private readonly Panel _shell = new();
    private readonly TableLayoutPanel _layout = new();
    private readonly Panel _titleBar = new();
    private readonly Panel _captionButtonHost = new();
    private readonly Panel _contentHost = new();
    private readonly PictureBox _icon = new();
    private readonly Label _title = new();
    private readonly CaptionButton _minimizeButton = new(CaptionButtonKind.Minimize);
    private readonly CaptionButton _maximizeButton = new(CaptionButtonKind.Maximize);
    private readonly CaptionButton _closeButton = new(CaptionButtonKind.Close);
    private readonly bool _canMaximize;
    private readonly bool _canMinimize;
    private bool _disposed;

    private ThemedWindowChrome(Form form, Icon appIcon, bool canMaximize, bool canMinimize)
    {
        _form = form;
        _canMaximize = canMaximize;
        _canMinimize = canMinimize;

        ConfigureForm();
        ApplyInitialSizeCompensation();
        BuildChrome(appIcon);
        WrapExistingContent();
        ApplyTheme(WindowsTheme.Current());

        _form.HandleCreated += OnHandleCreated;
        _form.HandleDestroyed += OnHandleDestroyed;
        _form.Resize += OnFormResize;
        _form.TextChanged += OnFormTextChanged;
        _form.FormClosed += OnFormClosed;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;

        if (_form.IsHandleCreated)
        {
            AssignHandle(_form.Handle);
        }
    }

    public static ThemedWindowChrome Install(Form form, Icon appIcon, bool canMaximize = true, bool canMinimize = true)
    {
        return new ThemedWindowChrome(form, appIcon, canMaximize, canMinimize);
    }

    public void ApplyTheme(ThemePalette theme)
    {
        _shell.BackColor = theme.Border;
        _layout.BackColor = theme.Window;
        _titleBar.BackColor = theme.Window;
        _captionButtonHost.BackColor = theme.Window;
        _contentHost.BackColor = theme.Window;
        _title.BackColor = theme.Window;
        _title.ForeColor = theme.Text;
        _icon.BackColor = theme.Window;

        _minimizeButton.ApplyTheme(theme, isDanger: false);
        _maximizeButton.ApplyTheme(theme, isDanger: false);
        _closeButton.ApplyTheme(theme, isDanger: true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmGetMinMaxInfo)
        {
            ApplyWorkingAreaMaximizeBounds(m.LParam);
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WmNcHitTest && _form.WindowState == FormWindowState.Normal)
        {
            var hit = HitTestResizeGrip(m.LParam);
            if (hit is not null)
            {
                m.Result = new IntPtr(hit.Value);
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void ConfigureForm()
    {
        _form.FormBorderStyle = FormBorderStyle.None;
        _form.Padding = Padding.Empty;
        _form.Region?.Dispose();
        _form.Region = null;
    }

    private void ApplyInitialSizeCompensation()
    {
        if (_form is not DeviceManagerForm)
        {
            return;
        }

        var extraHeight = Scale(DeviceManagerHeightCompensation);
        _form.MinimumSize = new Size(_form.MinimumSize.Width, _form.MinimumSize.Height + extraHeight);
        _form.Size = new Size(_form.Size.Width, _form.Size.Height + extraHeight);
    }

    private void BuildChrome(Icon appIcon)
    {
        _shell.Dock = DockStyle.Fill;
        _shell.Margin = Padding.Empty;
        _shell.Padding = new Padding(Scale(BorderThickness));

        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = Padding.Empty;
        _layout.ColumnCount = 1;
        _layout.RowCount = 2;
        _layout.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(TitleBarHeight)));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _titleBar.Dock = DockStyle.Fill;
        _titleBar.Margin = Padding.Empty;
        _titleBar.Padding = new Padding(Scale(18), 0, 0, 0);
        _titleBar.MouseDown += StartWindowDrag;
        _titleBar.DoubleClick += ToggleMaximize;

        _captionButtonHost.Dock = DockStyle.Right;
        _captionButtonHost.Margin = Padding.Empty;
        _captionButtonHost.Padding = Padding.Empty;
        _captionButtonHost.Height = Scale(TitleBarHeight);
        _captionButtonHost.Width = GetCaptionButtonHostWidth();
        _captionButtonHost.Resize += (_, _) => LayoutTitleBar();

        _icon.Image = appIcon.ToBitmap();
        _icon.SizeMode = PictureBoxSizeMode.Zoom;
        _icon.Size = new Size(Scale(IconSize), Scale(IconSize));
        _icon.Location = new Point(Scale(18), (Scale(TitleBarHeight) - Scale(IconSize)) / 2);
        _icon.MouseDown += StartWindowDrag;
        _icon.DoubleClick += ToggleMaximize;

        _title.Text = _form.Text;
        _title.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Location = new Point(Scale(48), 0);
        _title.Height = Scale(TitleBarHeight);
        _title.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _title.MouseDown += StartWindowDrag;
        _title.DoubleClick += ToggleMaximize;

        ConfigureCaptionButton(_minimizeButton, (_, _) => _form.WindowState = FormWindowState.Minimized);
        ConfigureCaptionButton(_maximizeButton, (_, _) => ToggleMaximize());
        ConfigureCaptionButton(_closeButton, (_, _) => _form.Close());
        _minimizeButton.Visible = _canMinimize;
        _maximizeButton.Visible = _canMaximize;

        _captionButtonHost.Controls.Add(_minimizeButton);
        _captionButtonHost.Controls.Add(_maximizeButton);
        _captionButtonHost.Controls.Add(_closeButton);

        _titleBar.Controls.Add(_title);
        _titleBar.Controls.Add(_icon);
        _titleBar.Controls.Add(_captionButtonHost);
        _titleBar.Resize += (_, _) => LayoutTitleBar();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.Margin = Padding.Empty;
        _contentHost.Padding = Padding.Empty;

        _layout.Controls.Add(_titleBar, 0, 0);
        _layout.Controls.Add(_contentHost, 0, 1);
        _shell.Controls.Add(_layout);

        LayoutTitleBar();
    }

    private void WrapExistingContent()
    {
        var existingControls = _form.Controls.Cast<Control>().ToArray();
        _form.Controls.Clear();
        foreach (var control in existingControls)
        {
            _contentHost.Controls.Add(control);
        }

        _form.Controls.Add(_shell);
    }

    private void ConfigureCaptionButton(CaptionButton button, EventHandler click)
    {
        button.TabStop = false;
        button.Size = new Size(Scale(CaptionButtonWidth), Scale(TitleBarHeight));
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Click += click;
    }

    private void LayoutTitleBar()
    {
        var buttonHeight = Scale(TitleBarHeight);
        var buttonWidth = Scale(CaptionButtonWidth);
        _captionButtonHost.Width = GetCaptionButtonHostWidth();
        _captionButtonHost.Height = buttonHeight;

        var right = _captionButtonHost.ClientSize.Width;
        _closeButton.SetBounds(right - buttonWidth, 0, buttonWidth, buttonHeight);
        right -= buttonWidth;

        if (_canMaximize)
        {
            _maximizeButton.SetBounds(right - buttonWidth, 0, buttonWidth, buttonHeight);
            right -= buttonWidth;
        }
        else
        {
            _maximizeButton.SetBounds(0, 0, 0, buttonHeight);
        }

        if (_canMinimize)
        {
            _minimizeButton.SetBounds(right - buttonWidth, 0, buttonWidth, buttonHeight);
        }
        else
        {
            _minimizeButton.SetBounds(0, 0, 0, buttonHeight);
        }

        _captionButtonHost.BringToFront();
        _title.Width = Math.Max(1, _captionButtonHost.Left - _title.Left - Scale(8));
    }

    private int GetCaptionButtonHostWidth()
    {
        var count = 1 + (_canMaximize ? 1 : 0) + (_canMinimize ? 1 : 0);
        return Scale(CaptionButtonWidth) * count;
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        _maximizeButton.Kind = _form.WindowState == FormWindowState.Maximized
            ? CaptionButtonKind.Restore
            : CaptionButtonKind.Maximize;
        LayoutTitleBar();
    }

    private int? HitTestResizeGrip(IntPtr lParam)
    {
        var point = _form.PointToClient(new Point(GetXLParam(lParam), GetYLParam(lParam)));
        var grip = Scale(ResizeGripThickness);
        var left = point.X <= grip;
        var right = point.X >= _form.ClientSize.Width - grip;
        var top = point.Y <= grip;
        var bottom = point.Y >= _form.ClientSize.Height - grip;

        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return null;
    }

    private void ApplyWorkingAreaMaximizeBounds(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var screen = Screen.FromHandle(_form.Handle);
        var workingArea = screen.WorkingArea;
        var bounds = screen.Bounds;

        info.MaxPosition = new NativePoint(
            Math.Abs(workingArea.Left - bounds.Left),
            Math.Abs(workingArea.Top - bounds.Top));
        info.MaxSize = new NativePoint(workingArea.Width, workingArea.Height);

        Marshal.StructureToPtr(info, lParam, true);
    }

    private void StartWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(_form.Handle, WmNcLButtonDown, HtCaption, 0);
    }

    private void ToggleMaximize(object? sender, EventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (!_canMaximize)
        {
            return;
        }

        _form.WindowState = _form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        AssignHandle(_form.Handle);
    }

    private void OnHandleDestroyed(object? sender, EventArgs e)
    {
        ReleaseHandle();
    }

    private void OnFormTextChanged(object? sender, EventArgs e)
    {
        _title.Text = _form.Text;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        Dispose();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            ApplyThemeSoon();
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        ApplyThemeSoon();
    }

    private void ApplyThemeSoon()
    {
        if (_disposed || _form.IsDisposed)
        {
            return;
        }

        if (_form.IsHandleCreated)
        {
            _form.BeginInvoke(() => ApplyTheme(WindowsTheme.Current()));
            return;
        }

        ApplyTheme(WindowsTheme.Current());
    }

    private int Scale(int value)
    {
        using var graphics = _form.CreateGraphics();
        return (int)Math.Round(value * graphics.DpiX / 96f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _form.HandleCreated -= OnHandleCreated;
        _form.HandleDestroyed -= OnHandleDestroyed;
        _form.Resize -= OnFormResize;
        _form.TextChanged -= OnFormTextChanged;
        _form.FormClosed -= OnFormClosed;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        ReleaseHandle();
        _icon.Image?.Dispose();
        _form.Region?.Dispose();
        _form.Region = null;
    }

    private static int GetXLParam(IntPtr lParam)
    {
        return unchecked((short)(long)lParam);
    }

    private static int GetYLParam(IntPtr lParam)
    {
        return unchecked((short)((long)lParam >> 16));
    }

    private enum CaptionButtonKind
    {
        Minimize,
        Maximize,
        Restore,
        Close
    }

    private sealed class CaptionButton : Control
    {
        private ThemePalette _theme = WindowsTheme.Current();
        private bool _isDanger;
        private bool _isHovered;
        private bool _isPressed;
        private CaptionButtonKind _kind;

        public CaptionButton(CaptionButtonKind kind)
        {
            _kind = kind;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CaptionButtonKind Kind
        {
            get => _kind;
            set
            {
                if (_kind == value)
                {
                    return;
                }

                _kind = value;
                Invalidate();
            }
        }

        public void ApplyTheme(ThemePalette theme, bool isDanger)
        {
            _theme = theme;
            _isDanger = isDanger;
            BackColor = theme.Window;
            ForeColor = theme.Text;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var backgroundColor = GetBackgroundColor();
            using var background = new SolidBrush(backgroundColor);
            e.Graphics.FillRectangle(background, ClientRectangle);

            using var pen = new Pen(_isDanger && _isHovered ? Color.White : _theme.Text, 1.45f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            var size = Math.Max(10, Math.Min(ClientSize.Width, ClientSize.Height) / 4);
            var x = (ClientSize.Width - size) / 2;
            var y = (ClientSize.Height - size) / 2;
            var icon = new Rectangle(x, y, size, size);

            switch (_kind)
            {
                case CaptionButtonKind.Minimize:
                    DrawMinimize(e.Graphics, pen, icon);
                    break;
                case CaptionButtonKind.Maximize:
                    DrawMaximize(e.Graphics, pen, icon);
                    break;
                case CaptionButtonKind.Restore:
                    DrawRestore(e.Graphics, pen, icon, backgroundColor);
                    break;
                case CaptionButtonKind.Close:
                    DrawClose(e.Graphics, pen, icon);
                    break;
            }
        }

        private Color GetBackgroundColor()
        {
            if (!_isHovered && !_isPressed)
            {
                return _theme.Window;
            }

            if (_isDanger)
            {
                return _isPressed ? ControlPaint.Dark(_theme.Danger) : _theme.Danger;
            }

            return _isPressed ? ControlPaint.Dark(_theme.SurfaceRaised) : _theme.SurfaceRaised;
        }

        private static void DrawMinimize(Graphics graphics, Pen pen, Rectangle icon)
        {
            var y = icon.Top + icon.Height * 0.65f;
            graphics.DrawLine(pen, icon.Left, y, icon.Right, y);
        }

        private static void DrawMaximize(Graphics graphics, Pen pen, Rectangle icon)
        {
            graphics.DrawRectangle(pen, icon);
        }

        private static void DrawRestore(Graphics graphics, Pen pen, Rectangle icon, Color backgroundColor)
        {
            var offset = Math.Max(3, icon.Width / 3);
            var back = new Rectangle(icon.Left + offset, icon.Top, icon.Width, icon.Height);
            var front = new Rectangle(icon.Left, icon.Top + offset, icon.Width, icon.Height);
            graphics.DrawRectangle(pen, back);
            using var background = new SolidBrush(backgroundColor);
            graphics.FillRectangle(background, new Rectangle(front.Left + 1, front.Top + 1, front.Width - 1, front.Height - 1));
            graphics.DrawRectangle(pen, front);
        }

        private static void DrawClose(Graphics graphics, Pen pen, Rectangle icon)
        {
            graphics.DrawLine(pen, icon.Left, icon.Top, icon.Right, icon.Bottom);
            graphics.DrawLine(pen, icon.Right, icon.Top, icon.Left, icon.Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
