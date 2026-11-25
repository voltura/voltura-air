using System.Drawing;

namespace VolturaAir.Host;

internal sealed class ScreenViewCursorProjection(int desktopWidth, int desktopHeight)
{
    private Size _videoSize;

    public ScreenViewCursorUpdate? Update(
        ScreenViewCursorUpdate? change,
        ScreenViewCursorUpdate? latest,
        Size? encodedSize = null)
    {
        Size previousSize = _videoSize;
        _videoSize = encodedSize ?? previousSize;
        if (_videoSize.IsEmpty || (change is null && _videoSize == previousSize)) return null;

        ScreenViewCursorUpdate? cursor = previousSize.IsEmpty ? latest : change ?? latest;
        if (cursor is null) return null;

        double xScale = (double)_videoSize.Width / desktopWidth;
        double yScale = (double)_videoSize.Height / desktopHeight;
        return cursor with
        {
            X = (int)Math.Round(cursor.X * xScale),
            Y = (int)Math.Round(cursor.Y * yScale),
            HotSpotX = (int)Math.Round(cursor.HotSpotX * xScale),
            HotSpotY = (int)Math.Round(cursor.HotSpotY * yScale),
            Width = Math.Max(1, (int)Math.Round(cursor.Width * xScale)),
            Height = Math.Max(1, (int)Math.Round(cursor.Height * yScale)),
            PngBytes = previousSize.IsEmpty ? latest?.PngBytes : change?.PngBytes
        };
    }
}
