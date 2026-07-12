using VolturaAir.Host;

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

    private static string DescribeKeyboardInput(SendInputInjector.Input input)
    {
        var key = input.Data.Keyboard.VirtualKey.ToString("X2");
        var direction = (input.Data.Keyboard.Flags & 0x0002) == 0 ? "down" : "up";
        return $"{key}:{direction}";
    }

    private sealed class RecordingSendInputNative : ISendInputNative
    {
        public List<SendInputInjector.Input[]> Batches { get; } = new();

        public Queue<uint> AcceptedCounts { get; init; } = new();

        public uint Send(SendInputInjector.Input[] inputs, int size, out int win32Error)
        {
            Batches.Add(inputs.ToArray());
            win32Error = 5;
            return AcceptedCounts.Count > 0 ? AcceptedCounts.Dequeue() : (uint)inputs.Length;
        }
    }
}
