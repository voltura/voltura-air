using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private static void SetIcon(Window window)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        if (File.Exists(iconPath))
        {
            window.Icon = BitmapFrame.Create(new Uri(iconPath));
        }
    }

    private void SetSidebarAppIcon()
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        if (File.Exists(imagePath))
        {
            SidebarAppIcon.Source = BitmapFrame.Create(new Uri(imagePath));
        }
    }

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);
}
