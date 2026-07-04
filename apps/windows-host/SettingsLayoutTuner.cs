using System.Drawing;

namespace VolturaAir.Host;

internal sealed class SettingsLayoutTuner : IDisposable
{
    private const int LogicalEmbeddedPanelHeight = 580;
    private const int LogicalCandidateListMinimumHeight = 300;
    private const int LogicalScrollTrackWidth = 14;
    private const int LogicalScrollThumbWidth = 6;
    private const int LogicalScrollThumbInset = 4;
    private const int LogicalScrollContentGutter = 18;

    private readonly SettingsForm _form;
    private bool _isTuning;
    private bool _isScheduled;

    public SettingsLayoutTuner(SettingsForm form)
    {
        _form = form;
        _form.Shown += OnSettingsLayoutChanged;
        _form.VisibleChanged += OnSettingsLayoutChanged;
        _form.SizeChanged += OnSettingsLayoutChanged;
        _form.Layout += OnSettingsLayoutChanged;
    }

    public void Dispose()
    {
        _form.Shown -= OnSettingsLayoutChanged;
        _form.VisibleChanged -= OnSettingsLayoutChanged;
        _form.SizeChanged -= OnSettingsLayoutChanged;
        _form.Layout -= OnSettingsLayoutChanged;
    }

    public void TuneNow()
    {
        ScheduleTune();
    }

    private void OnSettingsLayoutChanged(object? sender, EventArgs e)
    {
        ScheduleTune();
    }

    private void ScheduleTune()
    {
        if (_isScheduled || _isTuning || _form.IsDisposed || !_form.IsHandleCreated)
        {
            return;
        }

        _isScheduled = true;
        try
        {
            _form.BeginInvoke((MethodInvoker)(() =>
            {
                _isScheduled = false;
                TuneLayout();
            }));
        }
        catch (InvalidOperationException)
        {
            _isScheduled = false;
        }
    }

    private void TuneLayout()
    {
        if (_isTuning || _form.IsDisposed)
        {
            return;
        }

        _isTuning = true;
        try
        {
            TuneEmbeddedPanelRows(_form);
            TuneConnectionPanelLists(_form);
            TuneScrollbars(_form);
            ReserveOuterPageScrollbarGutter(_form);
        }
        finally
        {
            _isTuning = false;
        }
    }

    private void TuneEmbeddedPanelRows(Control root)
    {
        var panelHeight = ScaleLogical(LogicalEmbeddedPanelHeight);
        foreach (var layout in FindDescendants(root).OfType<TableLayoutPanel>())
        {
            for (var row = 0; row < layout.RowStyles.Count; row += 1)
            {
                var control = layout.GetControlFromPosition(0, row);
                if (control is not DeviceManagerPanel && control is not ConnectionSettingsPanel)
                {
                    continue;
                }

                if (Math.Abs(layout.RowStyles[row].Height - panelHeight) < 0.1f && layout.RowStyles[row].SizeType == SizeType.Absolute)
                {
                    continue;
                }

                layout.RowStyles[row].SizeType = SizeType.Absolute;
                layout.RowStyles[row].Height = panelHeight;
            }
        }
    }

    private void TuneConnectionPanelLists(Control root)
    {
        var minimumHeight = ScaleLogical(LogicalCandidateListMinimumHeight);
        foreach (var control in FindDescendants(root))
        {
            if (control.GetType().Name != "ThemedCandidateListBox")
            {
                continue;
            }

            if (control.MinimumSize.Height == minimumHeight)
            {
                continue;
            }

            control.MinimumSize = new Size(control.MinimumSize.Width, minimumHeight);
        }
    }

    private void TuneScrollbars(Control root)
    {
        var trackWidth = ScaleLogical(LogicalScrollTrackWidth);
        var thumbWidth = ScaleLogical(LogicalScrollThumbWidth);
        var thumbInset = ScaleLogical(LogicalScrollThumbInset);

        foreach (var track in FindDescendants(root).OfType<Panel>())
        {
            if (track.Controls.Count != 1 || track.Controls[0] is not Panel thumb)
            {
                continue;
            }

            if (track.Width > ScaleLogical(24) || thumb.Width > ScaleLogical(16))
            {
                continue;
            }

            track.Width = trackWidth;
            thumb.Width = thumbWidth;
            thumb.Left = thumbInset;
        }
    }

    private void ReserveOuterPageScrollbarGutter(Control root)
    {
        foreach (var viewport in FindDescendants(root).OfType<Panel>())
        {
            var canvas = viewport.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Controls.OfType<TableLayoutPanel>().Any());
            var scrollTrack = viewport.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Controls.Count == 1 && panel.Controls[0] is Panel);
            if (canvas is null || scrollTrack is null || canvas.Controls.Count != 1 || canvas.Controls[0] is not TableLayoutPanel pageContent)
            {
                continue;
            }

            if (!pageContent.Controls.OfType<Control>().Any(control => control is DeviceManagerPanel or ConnectionSettingsPanel))
            {
                continue;
            }

            var reservedWidth = Math.Max(1, viewport.ClientSize.Width - scrollTrack.Width - ScaleLogical(LogicalScrollContentGutter));
            if (reservedWidth <= 1 || pageContent.Width == reservedWidth)
            {
                continue;
            }

            pageContent.MinimumSize = new Size(reservedWidth, 0);
            pageContent.Width = reservedWidth;
            var preferredHeight = pageContent.GetPreferredSize(new Size(reservedWidth, 0)).Height;
            pageContent.Height = Math.Max(1, preferredHeight);
            pageContent.PerformLayout();
            canvas.Width = reservedWidth;
            canvas.Height = pageContent.Height;
        }
    }

    private IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private int ScaleLogical(int value)
    {
        using var graphics = _form.CreateGraphics();
        return (int)Math.Round(value * graphics.DpiX / 96f);
    }
}
