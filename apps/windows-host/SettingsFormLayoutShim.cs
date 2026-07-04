using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VolturaAir.Host;

internal static class SettingsFormLayoutShim
{
    private static readonly HashSet<SettingsForm> AttachedForms = new();
    private static readonly HashSet<Button> AttachedButtons = new();
    private static readonly HashSet<Control> DetachedWheelControls = new();
    private static readonly HashSet<SettingsForm> UpdatingForms = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => AttachSettingsForms();
    }

    private static void AttachSettingsForms()
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
        {
            if (AttachedForms.Add(form))
            {
                form.Disposed += (_, _) =>
                {
                    AttachedForms.Remove(form);
                    UpdatingForms.Remove(form);
                };
                form.Shown += (_, _) => FixSettingsForm(form);
                form.VisibleChanged += (_, _) => FixSettingsForm(form);
                form.SizeChanged += (_, _) => ScheduleFix(form);
            }

            AttachNavigationButtons(form);
        }
    }

    private static void AttachNavigationButtons(SettingsForm form)
    {
        foreach (var button in Descendants(form).OfType<Button>())
        {
            if (!AttachedButtons.Add(button))
            {
                continue;
            }

            button.Disposed += (_, _) => AttachedButtons.Remove(button);
            button.Click += (_, _) => FixSettingsForm(form);
        }
    }

    private static void ScheduleFix(SettingsForm form)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            return;
        }

        form.BeginInvoke((System.Windows.Forms.MethodInvoker)(() => FixSettingsForm(form)));
    }

    private static void FixSettingsForm(SettingsForm form)
    {
        if (UpdatingForms.Contains(form) || form.IsDisposed || !form.IsHandleCreated)
        {
            return;
        }

        var changed = false;
        UpdatingForms.Add(form);
        try
        {
            DetachOuterPageWheelHandlers(form);
            foreach (var table in Descendants(form).OfType<TableLayoutPanel>())
            {
                for (var row = 0; row < table.RowStyles.Count; row += 1)
                {
                    var control = table.GetControlFromPosition(0, row);
                    if (control is DeviceManagerPanel)
                    {
                        changed |= SetRowHeight(form, table, row, control, 480);
                    }
                    else if (control is ConnectionSettingsPanel)
                    {
                        changed |= SetRowHeight(form, table, row, control, 560);
                    }

                    if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                    {
                        changed |= SetRowHeight(form, table, row, control, 264);
                    }
                }
            }

            changed |= NormalizeConnectionSaveButton(form);

            if (changed)
            {
                form.GetType().GetMethod("RefreshPageScrollLayout", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, null);
            }

            HideOuterScrollbarForEmbeddedPages(form);
        }
        finally
        {
            UpdatingForms.Remove(form);
        }
    }

    private static void DetachOuterPageWheelHandlers(SettingsForm form)
    {
        var method = form.GetType().GetMethod("OnPageMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            return;
        }

        var handler = (MouseEventHandler)Delegate.CreateDelegate(typeof(MouseEventHandler), form, method);
        foreach (var page in Descendants(form).OfType<TableLayoutPanel>().Where(IsEmbeddedPage))
        {
            DetachWheel(page, handler);
            if (page.Parent is Control canvas)
            {
                DetachWheel(canvas, handler);
                if (canvas.Parent is Control viewport)
                {
                    DetachWheel(viewport, handler);
                }
            }
        }
    }

    private static void DetachWheel(Control control, MouseEventHandler handler)
    {
        foreach (var candidate in DescendantsAndSelf(control))
        {
            if (!DetachedWheelControls.Add(candidate))
            {
                continue;
            }

            candidate.Disposed += (_, _) => DetachedWheelControls.Remove(candidate);
            candidate.MouseWheel -= handler;
        }
    }

    private static bool SetRowHeight(Control form, TableLayoutPanel table, int row, Control control, int logicalHeight)
    {
        var height = Scale(form, logicalHeight);
        var changed = table.RowStyles[row].SizeType != SizeType.Absolute || Math.Abs(table.RowStyles[row].Height - height) > 0.1f || control.Height != height;
        if (!changed)
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

    private static bool NormalizeConnectionSaveButton(Control form)
    {
        var changed = false;
        foreach (var panel in Descendants(form).OfType<ConnectionSettingsPanel>())
        {
            foreach (var button in Descendants(panel).OfType<Button>().Where(button => button.Text == "Save"))
            {
                var width = Scale(form, 180);
                if (button.Dock != DockStyle.Right || button.Width != width)
                {
                    button.Dock = DockStyle.Right;
                    button.Width = width;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static void HideOuterScrollbarForEmbeddedPages(Control form)
    {
        foreach (var page in Descendants(form).OfType<TableLayoutPanel>().Where(IsEmbeddedPage))
        {
            if (page.Parent is not Panel canvas || canvas.Parent is not Panel viewport)
            {
                continue;
            }

            var track = viewport.Controls.OfType<Panel>().FirstOrDefault(panel => !ReferenceEquals(panel, canvas) && panel.Controls.Count == 1 && panel.Controls[0] is Panel);
            if (track is not null)
            {
                track.Visible = false;
            }

            canvas.Location = Point.Empty;
        }
    }

    private static bool IsEmbeddedPage(TableLayoutPanel table)
    {
        return table.Controls.OfType<Control>().Any(control => control is DeviceManagerPanel or ConnectionSettingsPanel);
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in Descendants(root))
        {
            yield return descendant;
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
