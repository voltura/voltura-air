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
    private static readonly Dictionary<SettingsForm, Control?> LastTunedPage = new();
    private static readonly Dictionary<SettingsForm, Size> LastTunedSize = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TuneOpenSettingsForms();
    }

    private static void TuneOpenSettingsForms()
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
        {
            TuneIfNeeded(form);
        }
    }

    private static void TuneIfNeeded(SettingsForm form)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            return;
        }

        var pageContent = FindEmbeddedPageContent(form);
        var size = form.ClientSize;
        if (LastTunedPage.TryGetValue(form, out var lastPage) && ReferenceEquals(lastPage, pageContent) && LastTunedSize.TryGetValue(form, out var lastSize) && lastSize == size)
        {
            return;
        }

        LastTunedPage[form] = pageContent;
        LastTunedSize[form] = size;

        if (pageContent is null)
        {
            return;
        }

        TuneEmbeddedPage(form, pageContent);
    }

    private static void TuneEmbeddedPage(SettingsForm form, TableLayoutPanel pageContent)
    {
        var panelHeight = ScaleLogical(form, EmbeddedPanelHeight);
        var listHeight = ScaleLogical(form, NetworkListHeight);
        for (var row = 0; row < pageContent.RowStyles.Count; row += 1)
        {
            var control = pageContent.GetControlFromPosition(0, row);
            if (control is DeviceManagerPanel or ConnectionSettingsPanel)
            {
                pageContent.RowStyles[row].SizeType = SizeType.Absolute;
                pageContent.RowStyles[row].Height = panelHeight;
                control.Height = panelHeight;
            }
        }

        foreach (var layout in Descendants(pageContent).OfType<TableLayoutPanel>())
        {
            for (var row = 0; row < layout.RowStyles.Count; row += 1)
            {
                var control = layout.GetControlFromPosition(0, row);
                if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                {
                    layout.RowStyles[row].SizeType = SizeType.Absolute;
                    layout.RowStyles[row].Height = listHeight;
                    control.MinimumSize = new Size(0, listHeight);
                    control.Height = listHeight;
                }
            }
        }

        ResizePage(form, pageContent);
        TuneScrollbarWidth(form);
    }

    private static void ResizePage(Control form, TableLayoutPanel pageContent)
    {
        if (pageContent.Parent is not Panel canvas || canvas.Parent is not Panel viewport)
        {
            return;
        }

        var width = Math.Max(1, viewport.ClientSize.Width - ScaleLogical(form, PageWidthGutter));
        pageContent.MinimumSize = new Size(width, 0);
        pageContent.Width = width;
        var height = pageContent.GetPreferredSize(new Size(width, 0)).Height;
        pageContent.Height = Math.Max(1, height);
        pageContent.PerformLayout();
        canvas.SetBounds(0, 0, width, pageContent.Height);

        var scrollTrack = viewport.Controls.OfType<Panel>().FirstOrDefault(panel => !ReferenceEquals(panel, canvas) && panel.Controls.Count == 1 && panel.Controls[0] is Panel);
        if (scrollTrack is not null && pageContent.Height <= viewport.ClientSize.Height)
        {
            scrollTrack.Visible = false;
        }
    }

    private static void TuneScrollbarWidth(Control form)
    {
        var trackWidth = ScaleLogical(form, ScrollTrackWidth);
        var thumbWidth = ScaleLogical(form, ScrollThumbWidth);
        var thumbLeft = ScaleLogical(form, ScrollThumbLeft);
        foreach (var panel in Descendants(form).OfType<Panel>())
        {
            if (panel.Controls.Count != 1 || panel.Controls[0] is not Panel child || panel.Width > ScaleLogical(form, 22) || child.Width > ScaleLogical(form, 10))
            {
                continue;
            }

            panel.Width = trackWidth;
            child.Width = thumbWidth;
            child.Left = thumbLeft;
        }
    }

    private static TableLayoutPanel? FindEmbeddedPageContent(Control form)
    {
        return Descendants(form).OfType<TableLayoutPanel>().FirstOrDefault(table => table.Controls.OfType<Control>().Any(control => control is DeviceManagerPanel or ConnectionSettingsPanel));
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
