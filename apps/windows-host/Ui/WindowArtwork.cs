using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace VolturaAir.Host.Ui;

internal static class WindowArtwork
{
    public static void Apply(Window window, Image sidebarImage)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        if (File.Exists(iconPath))
        {
            window.Icon = BitmapFrame.Create(new Uri(iconPath));
        }

        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        if (File.Exists(imagePath))
        {
            sidebarImage.Source = BitmapFrame.Create(new Uri(imagePath));
        }
    }
}
