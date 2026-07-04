using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VolturaAir.Host;

internal static class SettingsFormLayoutShim
{
    private static readonly HashSet<SettingsForm> AttachedForms = new();
    private static readonly HashSet<SettingsForm> RefreshingForms = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => AttachSettingsForms();
    }

    private static void AttachSettingsForms()
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
        {
            if (!AttachedForms.Add(form))
            {
                continue;
            }

            form.Disposed += (_, _) =>
            {
                AttachedForms.Remove(form);
                RefreshingForms.Remove(form);
            };
            form.ControlAdded += (_, _) => FixSettingsForm(form);
            form.Layout += (_, _) => FixSettingsForm(form);
            FixSettingsForm(form);
        }
    }

    private static void FixSettingsForm(SettingsForm form)
    {
        if (RefreshingForms.Contains(form))
        {
            return;
        }

        var changed = false;
        foreach (var table in Descendants(form).OfType<TableLayoutPanel>())
        {
            for (var row = 0; row < table.RowStyles.Count; row += 1)
            {
                var control = table.GetControlFromPosition(0, row);
                if (control is DeviceManagerPanel or ConnectionSettingsPanel)
                {
                    changed |= SetRowHeight(form, table, row, control, 500);
                }

                if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                {
                    changed |= SetRowHeight(form, table, row, control, 220);
                }
            }
        }

        if (!changed)
        {
            return;
        }

        RefreshingForms.Add(form);
        try
        {
            form.GetType().GetMethod("RefreshPageScrollLayout", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, null);
        }
        finally
        {
            RefreshingForms.Remove(form);
        }
    }

    private static bool SetRowHeight(Control form, TableLayoutPanel table, int row, Control control, int logicalHeight)
    {
        var height = Scale(form, logicalHeight);
        var changed = table.RowStyles[row].SizeType != SizeType.Absolute || Math.Abs(table.RowStyles[row].Height - height) > 0.1f;
        if (!changed && control.Height == height && control.MinimumSize.Height <= height)
        {
            return false;
        }

        table.RowStyles[row].SizeType = SizeType.Absolute;
        table.RowStyles[row].Height = height;
        control.MinimumSize = new Size(control.MinimumSize.Width, Math.Min(control.MinimumSize.Height, height));
        control.Height = height;
        table.PerformLayout();
        return true;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static int Scale(Control control, int value)
    {
        using var graphics = control.CreateGraphics();
        return (int)Math.Round(value * graphics.DpiX / 96f);
    }
}
