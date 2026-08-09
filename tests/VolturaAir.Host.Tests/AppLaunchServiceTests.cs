using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AppLaunchServiceTests
{
    [Theory]
    [InlineData(false, "app.exe %1", null, false)]
    [InlineData(true, null, null, false)]
    [InlineData(true, "", "", false)]
    [InlineData(true, "app.exe %1", null, true)]
    [InlineData(true, null, "{00000000-0000-0000-0000-000000000000}", true)]
    public void UriSchemeRequiresADeclaredProtocolAndAUsableHandler(
        bool declaresUrlProtocol,
        string? command,
        string? delegateExecute,
        bool expected)
    {
        Assert.Equal(
            expected,
            AppLaunchService.HasUsableUriSchemeRegistration(
                declaresUrlProtocol,
                command,
                delegateExecute));
    }
}
