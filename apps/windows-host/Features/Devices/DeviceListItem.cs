using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Devices;

internal sealed record DeviceListItem(
    string ClientId,
    string Name,
    string Status,
    bool IsConnected,
    string Activity,
    string Metadata)
{
    public PillBadgeTone StatusTone => IsConnected ? PillBadgeTone.Success : PillBadgeTone.Danger;
}
