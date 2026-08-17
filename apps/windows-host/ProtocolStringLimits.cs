namespace VolturaAir.Host;

internal static class ProtocolStringLimits
{
    public const int OperationId = 64;
    public const int HumanMessage = 240;
    public const int MachineCode = 80;
    public const int PcName = 120;
    public const int AdapterName = 256;
    public const int IpAddress = 64;
    public const int Url = 512;
    public const int BuildOrSessionId = 128;

    public static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    public static string? LimitOptional(string? value, int maximumLength) =>
        value is null ? null : Limit(value, maximumLength);
}
