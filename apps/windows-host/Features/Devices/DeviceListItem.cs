namespace VolturaAir.Host.Features.Devices;

internal sealed record DeviceListItem(
    string ClientId,
    string Name,
    string Status,
    string Activity,
    string Metadata);
