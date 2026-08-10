using VolturaAir.Host;
using System.Globalization;

namespace VolturaAir.Host.Tests;

public sealed class SendInputInjectorTests
{
    [Fact]
    public void SpecialKeySendsAltTabChordAsOneAtomicBatch()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.SpecialKey("Tab", new[] { "Alt" });

        var batch = Assert.Single(native.Batches);
        Assert.Equal(
            new[] { "12:down", "09:down", "09:up", "12:up" },
            batch.Select(DescribeKeyboardInput));
    }

    [Fact]
    public void SpecialKeyMapsInsertToTheWindowsVirtualKey()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.SpecialKey("Insert", []);

        var batch = Assert.Single(native.Batches);
        Assert.Equal(
            ["2D:down", "2D:up"],
            batch.Select(DescribeKeyboardInput));
    }

    [Theory]
    [InlineData(".", "BE")]
    [InlineData(",", "BC")]
    [InlineData(";", "BA")]
    public void SpecialKeyMapsCommonPunctuation(
        string key,
        string expectedVirtualKey)
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.SpecialKey(key, []);

        var batch = Assert.Single(native.Batches);
        Assert.Equal(
            [$"{expectedVirtualKey}:down", $"{expectedVirtualKey}:up"],
            batch.Select(DescribeKeyboardInput));
    }

    [Theory]
    [InlineData("Numpad0", "60")]
    [InlineData("Numpad1", "61")]
    [InlineData("Numpad2", "62")]
    [InlineData("Numpad3", "63")]
    [InlineData("Numpad4", "64")]
    [InlineData("Numpad5", "65")]
    [InlineData("Numpad6", "66")]
    [InlineData("Numpad7", "67")]
    [InlineData("Numpad8", "68")]
    [InlineData("Numpad9", "69")]
    [InlineData("NumpadMultiply", "6A")]
    [InlineData("NumpadAdd", "6B")]
    [InlineData("NumpadSubtract", "6D")]
    [InlineData("NumpadDecimal", "6E")]
    [InlineData("NumpadDivide", "6F")]
    public void SpecialKeyMapsNumpadKeys(string key, string expectedVirtualKey)
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.SpecialKey(key, []);

        var batch = Assert.Single(native.Batches);
        Assert.Equal(
            [$"{expectedVirtualKey}:down", $"{expectedVirtualKey}:up"],
            batch.Select(DescribeKeyboardInput));
    }

    [Fact]
    public void SpecialKeyRejectsUnknownKeyInsteadOfReportingAFalseSuccess()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        _ = Assert.Throws<ArgumentException>(() => injector.SpecialKey("NotAKey", []));
        Assert.Empty(native.Batches);
    }

    [Fact]
    public void PartialSpecialKeySendReleasesChordKeysAndPreservesOriginalFailure()
    {
        var native = new RecordingSendInputNative { AcceptedCounts = new Queue<uint>(new[] { 2u, 5u }) };
        using var injector = new SendInputInjector(native);

        var failure = Assert.Throws<InputDispatchException>(() => injector.SpecialKey("Tab", new[] { "Shift", "Alt" }));

        Assert.Equal("keyboard.special", failure.Operation);
        Assert.Equal(6, failure.RequestedCount);
        Assert.Equal(2, failure.AcceptedCount);
        Assert.True(failure.CleanupAttempted);
        Assert.True(failure.CleanupSucceeded);
        Assert.Equal(2, native.Batches.Count);
        Assert.Equal(
            new[] { "12:up", "10:up", "09:up", "11:up", "5B:up" },
            native.Batches[1].Select(DescribeKeyboardInput));
    }

    [Fact]
    public void TextInjectionUsesBoundedNativeBatches()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.TypeText(new string('a', 65));

        Assert.Equal(2, native.Batches.Count);
        Assert.Equal(128, native.Batches[0].Length);
        Assert.Equal(2, native.Batches[1].Length);
        Assert.All(native.Batches.SelectMany(batch => batch), input => Assert.Equal('a', input.Data.Keyboard.ScanCode));
    }

    [Fact]
    public void PartialTextBatchReleasesUnmatchedUnicodeKeyAndPreservesFailure()
    {
        var native = new RecordingSendInputNative { AcceptedCounts = new Queue<uint>([3u, 1u]) };
        using var injector = new SendInputInjector(native);

        var failure = Assert.Throws<InputDispatchException>(() => injector.TypeText("ab"));

        Assert.Equal("keyboard.text", failure.Operation);
        Assert.Equal(4, failure.RequestedCount);
        Assert.Equal(3, failure.AcceptedCount);
        Assert.True(failure.CleanupAttempted);
        Assert.True(failure.CleanupSucceeded);
        Assert.Equal(2, native.Batches.Count);
        var cleanup = Assert.Single(native.Batches[1]);
        Assert.Equal('b', cleanup.Data.Keyboard.ScanCode);
        Assert.Equal(0x0006u, cleanup.Data.Keyboard.Flags);
    }

    [Fact]
    public void TextBatchBoundaryKeepsSurrogatePairsTogether()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);
        var text = new string('a', 63) + "😀b";

        injector.TypeText(text);

        Assert.Equal(2, native.Batches.Count);
        Assert.Equal(126, native.Batches[0].Length);
        Assert.Equal(6, native.Batches[1].Length);
        Assert.Equal(text[63], native.Batches[1][0].Data.Keyboard.ScanCode);
        Assert.Equal(text[64], native.Batches[1][2].Data.Keyboard.ScanCode);
    }

    [Fact]
    public void ReusesSingleInputBufferForPointerMovement()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.MoveMouse(1, 2);
        injector.MoveMouse(3, 4);

        Assert.Equal(2, native.ArrayReferences.Count);
        Assert.Same(native.ArrayReferences[0], native.ArrayReferences[1]);
        _ = Assert.Single(native.ArrayReferences[0]);
        Assert.Equal(3, native.Batches[1][0].Data.Mouse.Dx);
        Assert.Equal(4, native.Batches[1][0].Data.Mouse.Dy);
    }

    [Fact]
    public void DirectButtonCombinesVirtualDesktopPositionAndActionAtomically()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.MouseButtonAt(1234, 5678, "left", "down");

        var input = Assert.Single(Assert.Single(native.Batches));
        Assert.Equal(1234, input.Data.Mouse.Dx);
        Assert.Equal(5678, input.Data.Mouse.Dy);
        Assert.Equal(0xC003u, input.Data.Mouse.Flags);
    }

    [Fact]
    public void DirectWheelUsesOneLockedBatchAtTheRequestedPosition()
    {
        var native = new RecordingSendInputNative();
        using var injector = new SendInputInjector(native);

        injector.ScrollAt(100, 200, 2, -3);

        var batch = Assert.Single(native.Batches);
        Assert.Equal(2, batch.Length);
        Assert.All(batch, input =>
        {
            Assert.Equal(100, input.Data.Mouse.Dx);
            Assert.Equal(200, input.Data.Mouse.Dy);
            Assert.Equal(0xC001u, input.Data.Mouse.Flags & 0xC001u);
        });
    }

    [Fact]
    public void PartialDirectButtonCleanupRetriesBothReleases()
    {
        var native = new RecordingSendInputNative { AcceptedCounts = new Queue<uint>([1u, 2u]) };
        using var injector = new SendInputInjector(native);

        InputDispatchException failure = Assert.Throws<InputDispatchException>(injector.ReleaseMouseButtons);

        Assert.True(failure.CleanupAttempted);
        Assert.True(failure.CleanupSucceeded);
        Assert.Equal(2, native.Batches.Count);
        Assert.Equal(new[] { 0x0004u, 0x0010u }, native.Batches[1].Select(input => input.Data.Mouse.Flags));
    }

    private static string DescribeKeyboardInput(SendInputInjector.Input input)
    {
        var key = input.Data.Keyboard.VirtualKey.ToString("X2", CultureInfo.InvariantCulture);
        var direction = (input.Data.Keyboard.Flags & 0x0002) == 0 ? "down" : "up";
        return $"{key}:{direction}";
    }

    private sealed class RecordingSendInputNative : ISendInputNative
    {
        public List<SendInputInjector.Input[]> Batches { get; } = new();

        public List<SendInputInjector.Input[]> ArrayReferences { get; } = new();

        public Queue<uint> AcceptedCounts { get; init; } = new();

        public uint Send(SendInputInjector.Input[] inputs, int size, out int win32Error)
        {
            ArrayReferences.Add(inputs);
            Batches.Add(inputs.ToArray());
            win32Error = 5;
            return AcceptedCounts.Count > 0 ? AcceptedCounts.Dequeue() : (uint)inputs.Length;
        }
    }
}
