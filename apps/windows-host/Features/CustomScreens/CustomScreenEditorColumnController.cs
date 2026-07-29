using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenEditorColumnController
{
    internal static void Attach(
        ColumnDefinition componentPalette,
        ColumnDefinition propertiesPanel,
        params GridSplitter[] splitters)
    {
        var widths = CustomScreenEditorSettings.PanelWidths();
        componentPalette.Width = new GridLength(widths.ComponentPalette);
        propertiesPanel.Width = new GridLength(widths.Properties);

        foreach (var splitter in splitters)
        {
            splitter.DragCompleted += PersistWidths;
        }

        void PersistWidths(object sender, DragCompletedEventArgs e)
        {
            if (!e.Canceled)
            {
                CustomScreenEditorSettings.SetPanelWidths(
                    componentPalette.ActualWidth,
                    propertiesPanel.ActualWidth);
            }
        }
    }
}
