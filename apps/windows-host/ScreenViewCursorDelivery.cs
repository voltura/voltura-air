namespace VolturaAir.Host;

internal sealed class ScreenViewCursorDelivery
{
    private IScreenViewWebRtcPeer? _peer;
    private string? _displayId;
    private ScreenViewCursorUpdate? _latest;
    private byte[]? _unsentRecord;
    private bool _includeShape;
    private bool _pending;
    private long _sequence;

    public void Publish(IScreenViewWebRtcPeer peer, string displayId, ScreenViewCursorUpdate? update)
    {
        if (_displayId != displayId)
        {
            _displayId = displayId;
            _latest = null;
            _unsentRecord = null;
            _includeShape = false;
            _pending = false;
        }

        if (!ReferenceEquals(_peer, peer))
        {
            _peer = peer;
            _unsentRecord = null;
            _includeShape = _latest?.PngBytes is not null;
            _pending = _latest is not null;
        }

        if (update is not null)
        {
            byte[]? png = update.PngBytes ?? _latest?.PngBytes;
            // Older controllers and peers already handle position-only records.
            if (png?.Length > ScreenViewRecordEncoder.MaximumCursorShapeBytes) png = null;
            if (update.PngBytes is not null) _includeShape = png is not null;
            _latest = update with { PngBytes = png };
            _unsentRecord = null;
            _pending = true;
        }

        if (!_pending || _latest is null) return;

        _unsentRecord ??= ScreenViewRecordEncoder.EncodeCursor(
            ++_sequence, _includeShape ? _latest : _latest with { PngBytes = null });
        if (peer.TrySendEvent(_unsentRecord))
        {
            _unsentRecord = null;
            _includeShape = false;
            _pending = false;
        }
    }
}
