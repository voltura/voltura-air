namespace VolturaAir.Host.Features.PhoneWebcam;

internal static class Nv12FrameComposer
{
    internal static void FitIntoCanvas(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int sourceLeft,
        int sourceTop,
        int sourceBufferHeight,
        Span<byte> target,
        int targetWidth,
        int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceStride < sourceLeft + sourceWidth ||
            sourceLeft < 0 || sourceTop < 0 || sourceBufferHeight < sourceTop + sourceHeight ||
            (sourceLeft & 1) != 0 || (sourceTop & 1) != 0 ||
            (sourceWidth & 1) != 0 || (sourceHeight & 1) != 0 ||
            targetWidth <= 0 || targetHeight <= 0 || (targetWidth & 1) != 0 || (targetHeight & 1) != 0)
        {
            throw new ArgumentException("NV12 dimensions and strides must be positive and even.");
        }
        int required = checked(sourceStride * sourceBufferHeight * 3 / 2);
        if (source.Length < required) throw new ArgumentException("The NV12 source buffer is truncated.", nameof(source));

        int targetBytes = checked(targetWidth * targetHeight * 3 / 2);
        if (target.Length < targetBytes) throw new ArgumentException("The NV12 target buffer is truncated.", nameof(target));
        target[..(targetWidth * targetHeight)].Fill(16);
        target.Slice(targetWidth * targetHeight, targetWidth * targetHeight / 2).Fill(128);
        double scale = Math.Min(1.0, Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight));
        int fittedWidth = Math.Max(2, ((int)(sourceWidth * scale)) & ~1);
        int fittedHeight = Math.Max(2, ((int)(sourceHeight * scale)) & ~1);
        int left = ((targetWidth - fittedWidth) / 2) & ~1;
        int top = ((targetHeight - fittedHeight) / 2) & ~1;

        if (fittedWidth == sourceWidth && fittedHeight == sourceHeight)
        {
            for (int row = 0; row < sourceHeight; ++row)
                source.Slice((sourceTop + row) * sourceStride + sourceLeft, sourceWidth)
                    .CopyTo(target.Slice((top + row) * targetWidth + left, sourceWidth));
        }
        else
        {
            Span<int> lumaColumns = stackalloc int[fittedWidth];
            for (int column = 0; column < fittedWidth; ++column)
                lumaColumns[column] = column * sourceWidth / fittedWidth;
            for (int row = 0; row < fittedHeight; ++row)
            {
                int sourceRow = row * sourceHeight / fittedHeight;
                Span<byte> targetRow = target.Slice((top + row) * targetWidth + left, fittedWidth);
                ReadOnlySpan<byte> sourceRowData = source.Slice((sourceTop + sourceRow) * sourceStride + sourceLeft, sourceWidth);
                for (int column = 0; column < fittedWidth; ++column)
                    targetRow[column] = sourceRowData[lumaColumns[column]];
            }
        }

        int sourceUv = sourceStride * sourceBufferHeight;
        int targetUv = targetWidth * targetHeight;
        int fittedChromaHeight = fittedHeight / 2;
        int sourceChromaHeight = sourceHeight / 2;
        int fittedChromaWidth = fittedWidth / 2;
        int sourceChromaWidth = sourceWidth / 2;
        if (fittedWidth == sourceWidth && fittedHeight == sourceHeight)
        {
            for (int row = 0; row < sourceHeight / 2; ++row)
                source.Slice(sourceUv + (sourceTop / 2 + row) * sourceStride + sourceLeft, sourceWidth)
                    .CopyTo(target.Slice(targetUv + (top / 2 + row) * targetWidth + left, sourceWidth));
        }
        else
        {
            Span<int> chromaColumns = stackalloc int[fittedChromaWidth];
            for (int column = 0; column < fittedChromaWidth; ++column)
                chromaColumns[column] = column * sourceChromaWidth / fittedChromaWidth * 2;
            for (int row = 0; row < fittedChromaHeight; ++row)
            {
                int sourceRow = row * sourceChromaHeight / fittedChromaHeight;
                ReadOnlySpan<byte> sourceRowData = source.Slice(
                    sourceUv + (sourceTop / 2 + sourceRow) * sourceStride + sourceLeft,
                    sourceWidth);
                Span<byte> targetRow = target.Slice(targetUv + (top / 2 + row) * targetWidth + left, fittedWidth);
                for (int column = 0; column < fittedChromaWidth; ++column)
                {
                    int sourceColumn = chromaColumns[column];
                    targetRow[column * 2] = sourceRowData[sourceColumn];
                    targetRow[column * 2 + 1] = sourceRowData[sourceColumn + 1];
                }
            }
        }
    }
}
