namespace VolturaAir.Host;

internal sealed class ScreenViewFramePacer(long timestampFrequency)
{
    private long _nextTimestamp;
    private int _framesPerSecond;
    private bool _hasDeadline;

    public bool ShouldEncode(long timestamp, int framesPerSecond)
    {
        if (timestamp < 0 || framesPerSecond is < 1 or > 60 || timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (_framesPerSecond != framesPerSecond)
            Reset();
        _framesPerSecond = framesPerSecond;
        if (_hasDeadline && timestamp < _nextTimestamp) return false;
        long interval = Math.Max(1, (long)Math.Ceiling(timestampFrequency / (double)framesPerSecond));
        if (!_hasDeadline)
        {
            _nextTimestamp = AddIntervals(timestamp, interval, 1);
        }
        else
        {
            long elapsedIntervals = ((timestamp - _nextTimestamp) / interval) + 1;
            _nextTimestamp = AddIntervals(_nextTimestamp, interval, elapsedIntervals);
        }
        _hasDeadline = true;
        return true;
    }

    private static long AddIntervals(long timestamp, long interval, long count) =>
        count > (long.MaxValue - timestamp) / interval ? long.MaxValue : timestamp + interval * count;

    public void Reset()
    {
        _nextTimestamp = 0;
        _framesPerSecond = 0;
        _hasDeadline = false;
    }
}
