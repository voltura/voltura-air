using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private const string ClientControlPreferenceTag = "ClientControlPreference";

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        QueueClientControlPreferenceInjection();
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        QueueClientControlPreferenceInjection();
    }

    private void QueueClientControlPreferenceInjection()
    {
        Dispatcher.BeginInvoke(InjectClientControlPreference, DispatcherPriority.Background);
    }

    private void InjectClientControlPreference()
    {
        if (_activePage != HostPage.Preferences ||
            PageContent.Content is not ScrollViewer { Content: StackPanel panel } ||
            ContainsClientControlPreference(panel))
        {
            return;
        }

        var allowClientControl = CreateCheckBox("Allow paired devices to control Voltura Air host", AppClientControlSettings.IsEnabled());
        allowClientControl.Tag = ClientControlPreferenceTag;
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);

        var note = CreateMutedText("When this is off, paired devices may stay connected for status checks but cannot send input, launch apps, change device settings, control audio, or request PC sleep.");
        note.Tag = ClientControlPreferenceTag;

        var insertIndex = FindGlobalPermissionsHeadingIndex(panel);
        if (insertIndex >= 0)
        {
            panel.Children.Insert(insertIndex + 1, note);
            panel.Children.Insert(insertIndex + 1, allowClientControl);
            return;
        }

        panel.Children.Add(allowClientControl);
        panel.Children.Add(note);
    }

    private static bool ContainsClientControlPreference(StackPanel panel)
    {
        return panel.Children
            .OfType<FrameworkElement>()
            .Any(element => Equals(element.Tag, ClientControlPreferenceTag));
    }

    private static int FindGlobalPermissionsHeadingIndex(StackPanel panel)
    {
        for (var index = 0; index < panel.Children.Count; index += 1)
        {
            if (panel.Children[index] is TextBlock { Text: "Global permissions" })
            {
                return index;
            }
        }

        return -1;
    }
}
