using System.Drawing;
using System.Runtime.CompilerServices;

namespace VolturaAir.Host;

internal static class SettingsLayoutTuner
{
    private const int EmbeddedPanelHeight = 500;
    private const int NetworkListHeight = 220;
    private const int ScrollTrackWidth = 18;
    private const int ScrollThumbWidth = 8;
    private const int ScrollThumbLeft = 5;
    private const int PageWidthGutter = 28;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TuneOpenSettingsForms();
    }

    private static void TuneOpenSettingsForms()
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>())
        {
            TuneForm(form);
        }
    }

    private static void TuneForm(SettingsForm form)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            return;
        }

        var panelHeight = ScaleLogical(form, EmbeddedPanelHeight);
        var listHeight = ScaleLogical(form, NetworkListHeight);
        foreach (var layout in Descendants(form).OfType<TableLayoutPanel>())
        {
            for (var row = 0; row < layout.RowStyles.Count; row += 1)
            {
                var control = layout.GetControlFromPosition(0, row);
                if (control is DeviceManagerPanel or ConnectionSettingsPanel)
                {
                    layout.RowStyles[row].SizeType = SizeType.Absolute;
                    layout.RowStyles[row].Height = panelHeight;
                    control.Height = panelHeight;
                    layout.PerformLayout();
                    ResizePage(layout, form);
                }

                if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                {
                    layout.RowStyles[row].SizeType = SizeType.Absolute;
                    layout.RowStyles[row].Height = listHeight;
                    control.MinimumSize = new Size(0, listHeight);
                    control.Height = listHeight;
                    layout.PerformLayout();
                }
            }
        }

        foreach (var panel in Descendants(form).OfType<Panel>())
        {
            if (panel.Controls.Count == 1 && panel.Controls[0] is Panel child && panel.Width <= ScaleLogical(form, 20) && child.Width <= ScaleLogical(form, 8))
            {
                panel.Width = ScaleLogical(form, ScrollTrackWidth);
                child.Width = ScaleLogical(form, ScrollThumbWidth);
                child.Left = ScaleLogical(form, ScrollThumbLeft);
            }
        }
    }

    private static void ResizePage(TableLayoutPanel pageContent, Control form)
    {
        if (pageContent.Parent is not Panel canvas || canvas.Parent is not Panel viewport)
        {
            return;
        }

        var width = Math.Max(1, viewport.ClientSize.Width - ScaleLogical(form, PageWidthGutter));
        if (pageContent.Width != width)
        {
            pageContent.MinimumSize = new Size(width, 0);
            pageContent.Width = width;
        }

        var preferredHeight = pageContent.GetPreferredSize(new Size(width, 0)).Height;
        pageContent.Height = Math.Max(1, preferredHeight);
        canvas.Width = width;
        canvas.Height = pageContent.Height;
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

    private static int ScaleLogical(Control control, int value)
    {
        using var graphics = control.CreateGraphics();
        return (int)Math.Round(value * graphics.DpiX / 96f);
    }
}
