using System.Drawing;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class CustomPointerServiceTests : IsolatedHostSettingsTest
{
    [Theory]
    [InlineData(1, 32)]
    [InlineData(6, 112)]
    [InlineData(15, 256)]
    [InlineData(-3, 32)]
    [InlineData(99, 256)]
    public void MapsPointerSizeToWindowsCursorCanvas(int size, int expectedCanvasSize)
    {
        Assert.Equal(expectedCanvasSize, CustomPointerService.GetCanvasSize(size));
    }

    [Fact]
    public void PreservesAlphaAndBlackOutlineWhenRecoloring()
    {
        using var bitmap = new Bitmap(3, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(0, 255, 0, 191));
        bitmap.SetPixel(1, 0, Color.FromArgb(255, 12, 12, 12));
        bitmap.SetPixel(2, 0, Color.FromArgb(128, 255, 0, 191));

        CustomPointerService.Recolor(bitmap, 0x12A894);

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(Color.FromArgb(255, 12, 12, 12), bitmap.GetPixel(1, 0));
        Assert.Equal(Color.FromArgb(128, 0x12, 0xA8, 0x94), bitmap.GetPixel(2, 0));
    }

    [Fact]
    public void ScalesTemplateHotspotWithCursorFrame()
    {
        Assert.Equal((16u, 32u), CustomPointerService.ScaleHotspot(32, 64, 256, 128));
    }

    [Fact]
    public void AppliesAndRestoresTheNativeCursorScheme()
    {
        using var service = new CustomPointerService();

        service.Apply(new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor));
        service.Restore();
    }

    [Fact]
    public void RecoveryMonitoringTracksTheActivePointerAndPreference()
    {
        var useRecoveryMonitoring = true;
        var starts = 0;
        var stops = 0;
        using var service = new CustomPointerService(
            () => useRecoveryMonitoring,
            () => starts += 1,
            () => stops += 1);

        service.Apply(new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor));
        Assert.Equal(1, starts);
        Assert.Equal(0, stops);

        useRecoveryMonitoring = false;
        service.RefreshRecoveryMonitoring();
        Assert.Equal(1, starts);
        Assert.Equal(1, stops);

        useRecoveryMonitoring = true;
        service.RefreshRecoveryMonitoring();
        Assert.Equal(2, starts);

        service.Apply(new CustomPointerSettings(false, 6, AppPointerSettings.DefaultCustomPointerColor));
        Assert.Equal(2, stops);
    }

    [Fact]
    public void CursorTemplateKeepsItsTransparentBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "CustomPointerTemplates", "Arrow.cur");
        using var template = CustomPointerService.LoadTemplateBitmap(path, "Arrow");

        Assert.Equal(0, template.GetPixel(template.Width - 1, template.Height - 1).A);
    }

    [Fact]
    public void LaserPointerHasTransparentCornersGlowRingAndDarkCenter()
    {
        using var laser = CustomPointerService.CreateLaserPointerBitmap();

        Assert.Equal(0, laser.GetPixel(0, 0).A);
        Assert.InRange(laser.GetPixel(20, laser.Height / 2).A, 1, 254);
        Assert.Equal(Color.FromArgb(255, 255, 18, 28), laser.GetPixel(43, laser.Height / 2));
        Assert.Equal(Color.FromArgb(255, 168, 0, 8), laser.GetPixel(laser.Width / 2, laser.Height / 2));
    }

    [Theory]
    [InlineData(1, 32)]
    [InlineData(6, 112)]
    [InlineData(15, 256)]
    public void LaserPointerUsesTheSharedPointerSizeScale(int size, int expectedPixels)
    {
        using var laser = CustomPointerService.CreateLaserPointerBitmap(
            new PresentationLaserPointerSettings(size, PresentationLaserColor.Red));

        Assert.Equal(expectedPixels, laser.Width);
        Assert.Equal(expectedPixels, laser.Height);
        Assert.Equal(0, laser.GetPixel(0, 0).A);
    }

    [Theory]
    [InlineData(PresentationLaserColor.Red, 168, 0, 8)]
    [InlineData(PresentationLaserColor.Green, 8, 158, 82)]
    [InlineData(PresentationLaserColor.Blue, 8, 118, 220)]
    public void LaserPointerAppliesTheSelectedSemanticColor(
        PresentationLaserColor color,
        int expectedRed,
        int expectedGreen,
        int expectedBlue)
    {
        using var laser = CustomPointerService.CreateLaserPointerBitmap(
            new PresentationLaserPointerSettings(6, color));

        Assert.Equal(
            Color.FromArgb(255, expectedRed, expectedGreen, expectedBlue),
            laser.GetPixel(laser.Width / 2, laser.Height / 2));
    }
}
