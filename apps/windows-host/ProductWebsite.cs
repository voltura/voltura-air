namespace VolturaAir.Host;

internal static class ProductWebsite
{
    public const string Url = "https://voltura.se/air/";

    public static void Open()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Url,
            UseShellExecute = true
        });
    }
}
