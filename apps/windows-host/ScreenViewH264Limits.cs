namespace VolturaAir.Host;

internal readonly record struct ScreenViewH264Limits(
    int MaximumMacroblocksPerFrame,
    int MaximumMacroblocksPerSecond,
    int MaximumBitrate)
{
    public static ScreenViewH264Limits Level52 { get; } = new(36_864, 2_073_600, 240_000_000);
}
