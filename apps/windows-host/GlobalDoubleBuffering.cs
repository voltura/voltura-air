using System.Reflection;
using System.Runtime.CompilerServices;

namespace VolturaAir.Host;

internal static class GlobalDoubleBuffering
{
    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly HashSet<Control> BufferedControls = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => EnableForOpenForms();
    }

    private static void EnableForOpenForms()
    {
        foreach (Form form in Application.OpenForms)
        {
            EnableForTree(form);
        }
    }

    private static void EnableForTree(Control root)
    {
        foreach (var control in DescendantsAndSelf(root))
        {
            if (!BufferedControls.Add(control))
            {
                continue;
            }

            control.Disposed += (_, _) => BufferedControls.Remove(control);
            EnableForControl(control);
        }
    }

    private static void EnableForControl(Control control)
    {
        try
        {
            DoubleBufferedProperty?.SetValue(control, true);
        }
        catch (ArgumentException)
        {
        }
        catch (TargetInvocationException)
        {
        }
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}
