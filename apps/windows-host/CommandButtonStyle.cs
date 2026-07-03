using System.Drawing;

namespace VolturaAir.Host;

internal enum CommandButtonKind
{
    Normal,
    Primary,
    Danger
}

internal static class CommandButtonStyle
{
    public const int ActionRowHeight = 72;
    public const int ButtonHeight = 64;
    public const int ActionTopPadding = ActionRowHeight - ButtonHeight;
    public const int ButtonGap = 8;

    public static void Configure(Button button)
    {
        button.Font = new Font("Segoe UI", 9f);
        button.Padding = Padding.Empty;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.AutoEllipsis = true;
        button.UseVisualStyleBackColor = false;
    }

    public static void ApplyTheme(Button button, ThemePalette theme, CommandButtonKind kind)
    {
        var primary = kind == CommandButtonKind.Primary;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? theme.Accent : theme.SurfaceRaised;
        button.ForeColor = primary ? theme.AccentText : kind == CommandButtonKind.Danger ? theme.Danger : theme.Text;
        button.FlatAppearance.BorderColor = primary ? theme.Accent : theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(theme.Accent) : ControlPaint.Light(theme.SurfaceRaised);
        button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(theme.Accent) : ControlPaint.Dark(theme.SurfaceRaised);
    }
}
