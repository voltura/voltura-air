using System.Drawing;
using System.Runtime.CompilerServices;

namespace VolturaAir.Host;

internal static class SettingsFormLayoutShim
{
    private static readonly HashSet<SettingsForm> AttachedForms = new();

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

            form.Disposed += (_, _) => AttachedForms.Remove(form);
            form.ControlAdded += (_, _) => FixSettingsForm(form);
            form.Layout += (_, _) => FixSettingsForm(form);
            FixSettingsForm(form);
        }
    }

    private static void FixSettingsForm(Control form)
    {
        foreach (var table in Descendants(form).OfType<TableLayoutPanel>())
        {
            for (var row = 0; row < table.RowStyles.Count; row += 1)
            {
                var control = table.GetControlFromPosition(0, row);
                if (control is DeviceManagerPanel or ConnectionSettingsPanel)
                {
                    table.RowStyles[row].SizeType = SizeType.Absolute;
                    table.RowStyles[row].Height = Scale(form, 500);
                    control.Height = Scale(form, 500);
                }

                if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                {
                    table.RowStyles[row].SizeType = SizeType.Absolute;
                    table.RowStyles[row].Height = Scale(form, 220);
                    control.MinimumSize = new Size(0, Scale(form, 220));
                    control.Height = Scale(form, 220);
                }
            }
        }
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
