using System.Drawing;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomPointerServiceTests
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
    public void CursorTemplateKeepsItsTransparentBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "CustomPointerTemplates", "Arrow.cur");
        using var template = CustomPointerService.LoadTemplateBitmap(path, "Arrow");

        Assert.Equal(0, template.GetPixel(template.Width - 1, template.Height - 1).A);
    }
}
