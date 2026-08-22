namespace VolturaAir.Host;

internal static class ProductWebsite
{
    public const string Url = "https://voltura.se/air/";
    public const string CustomScreenLibraryUrl = "https://voltura.se/air/screens/";
    public const string CustomScreenUploadUrl = "https://voltura.se/air/screens/upload.php";
    public const string PrivacyUrl = "https://github.com/voltura/voltura-air/blob/main/PRIVACY.md";

    public static void Open() => OpenUrl(Url);

    public static void OpenCustomScreenLibrary() => OpenUrl(CustomScreenLibraryUrl);

    public static void OpenCustomScreenUpload() => OpenUrl(CustomScreenUploadUrl);

    public static void OpenPrivacy() => OpenUrl(PrivacyUrl);

    private static void OpenUrl(string url)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
