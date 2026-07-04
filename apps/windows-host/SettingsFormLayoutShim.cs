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
                        changed |= SetRowHeight(form, table, row, control, 520);
                    }

                    if (control is not null && control.GetType().Name == "ThemedCandidateListBox")
                    {
                        changed |= SetRowHeight(form, table, row, control, 300);
                    }
                }
            }

            changed |= MoveConnectionSaveButton(form);

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

    private static bool MoveConnectionSaveButton(Control form)
    {
        var changed = false;
        foreach (var panel in Descendants(form).OfType<ConnectionSettingsPanel>())
        {
            var saveButton = Descendants(panel).OfType<Button>().FirstOrDefault(button => button.Text == "Save");
            var portPanel = Descendants(panel).OfType<TableLayoutPanel>().FirstOrDefault(IsPortPanel);
            if (saveButton is null || portPanel is null)
            {
                continue;
            }

            changed |= EnsurePortPanelSaveRows(form, portPanel);
            changed |= CollapseOriginalSaveRow(saveButton, portPanel);

            if (!ReferenceEquals(saveButton.Parent, portPanel))
            {
                saveButton.Parent?.Controls.Remove(saveButton);
                portPanel.Controls.Add(saveButton, 0, 7);
                changed = true;
            }
            else
            {
                var position = portPanel.GetPositionFromControl(saveButton);
                if (position.Row != 7 || position.Column != 0)
                {
                    portPanel.Controls.Remove(saveButton);
                    portPanel.Controls.Add(saveButton, 0, 7);
                    changed = true;
                }
            }

            var buttonWidth = Scale(form, 180);
            var buttonHeight = Scale(form, CommandButtonStyle.ButtonHeight);
            var topMargin = Scale(form, CommandButtonStyle.ActionTopPadding);
            if (saveButton.Dock != DockStyle.Right || saveButton.Width != buttonWidth || saveButton.Height != buttonHeight || saveButton.Margin.Top != topMargin)
            {
                saveButton.Dock = DockStyle.Right;
                saveButton.Width = buttonWidth;
                saveButton.Height = buttonHeight;
                saveButton.Margin = new Padding(0, topMargin, 0, 0);
                changed = true;
            }
        }

        return changed;
    }

    private static bool EnsurePortPanelSaveRows(Control form, TableLayoutPanel portPanel)
    {
        var changed = false;
        if (portPanel.RowCount < 8)
        {
            portPanel.RowCount = 8;
            changed = true;
        }

        while (portPanel.RowStyles.Count < 8)
        {
            portPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            changed = true;
        }

        changed |= SetRowStyle(portPanel.RowStyles[6], SizeType.Percent, 100f);
        changed |= SetRowStyle(portPanel.RowStyles[7], SizeType.Absolute, Scale(form, CommandButtonStyle.ActionRowHeight));
        return changed;
    }

    private static bool CollapseOriginalSaveRow(Control saveButton, TableLayoutPanel portPanel)
    {
        var oldParent = saveButton.Parent;
        if (oldParent is null || ReferenceEquals(oldParent, portPanel) || oldParent.Parent is not TableLayoutPanel oldLayout)
        {
            return false;
        }

        var changed = false;
        var position = oldLayout.GetPositionFromControl(oldParent);
        if (position.Row >= 0 && position.Row < oldLayout.RowStyles.Count)
        {
            changed |= SetRowStyle(oldLayout.RowStyles[position.Row], SizeType.Absolute, 0f);
        }

        if (oldParent.Visible)
        {
            oldParent.Visible = false;
            changed = true;
        }

        return changed;
    }

    private static bool SetRowStyle(RowStyle style, SizeType sizeType, float height)
    {
        var changed = style.SizeType != sizeType || Math.Abs(style.Height - height) > 0.1f;
        if (!changed)
        {
            return false;
        }

        style.SizeType = sizeType;
        style.Height = height;
        return true;
    }

    private static bool IsPortPanel(TableLayoutPanel table)
    {
        return table.Controls.OfType<Label>().Any(label => label.Text == "PORT") &&
            Descendants(table).OfType<Button>().Any(button => button.Text == "Manual port");
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
