using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class PreferencesSearchRegistry
{
    private readonly List<PreferenceSearchEntry> _entries = [];

    public void Clear() => _entries.Clear();

    public void RegisterSection(Expander section)
    {
        if (section.Header is string label)
        {
            Register(label, section, section);
        }
    }

    public SettingsCheckBox Register(SettingsCheckBox control, string? context = null)
    {
        Register(control.Label, control, control, context);
        return control;
    }

    public void RegisterLabel(
        TextBlock label,
        FrameworkElement focusTarget,
        string? context = null)
    {
        Register(label.Text, label, focusTarget, context);
    }

    public IReadOnlyList<PreferenceSearchResult> Match(string? query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        return [.. _entries
            .Where(entry => IsAvailable(entry.RevealTarget))
            .Where(entry => entry.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new PreferenceSearchResult(entry, BuildBreadcrumb(entry)))];
    }

    public static IReadOnlyList<Expander> FindContainingExpanders(DependencyObject target)
    {
        var expanders = new List<Expander>();
        for (var current = LogicalTreeHelper.GetParent(target);
             current is not null;
             current = LogicalTreeHelper.GetParent(current))
        {
            if (current is Expander expander)
            {
                expanders.Add(expander);
            }
        }

        expanders.Reverse();
        return expanders;
    }

    private void Register(
        string label,
        FrameworkElement revealTarget,
        FrameworkElement focusTarget,
        string? context = null)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            _entries.Add(new PreferenceSearchEntry(label, revealTarget, focusTarget, context));
        }
    }

    private static bool IsAvailable(DependencyObject target)
    {
        for (var current = target;
             current is not null;
             current = LogicalTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildBreadcrumb(PreferenceSearchEntry entry)
    {
        var segments = FindContainingExpanders(entry.RevealTarget)
            .Select(expander => expander.Header as string)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(entry.Context) &&
            !segments.Contains(entry.Context, StringComparer.Ordinal))
        {
            segments.Add(entry.Context);
        }

        return string.Join(" > ", segments);
    }
}

internal sealed record PreferenceSearchEntry(
    string Label,
    FrameworkElement RevealTarget,
    FrameworkElement FocusTarget,
    string? Context);

internal sealed class PreferenceSearchResult(
    PreferenceSearchEntry entry,
    string breadcrumb)
{
    public PreferenceSearchEntry Entry { get; } = entry;

    public string Label => Entry.Label;

    public string Breadcrumb { get; } = breadcrumb;
}
