namespace VolturaAir.Host.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppPermissionSettingsCollection
{
    public const string Name = "App permission settings";
}

public abstract class IsolatedHostSettingsTest : IDisposable
{
    private readonly IDisposable _settingsScope = HostSettingsRegistry.BeginIsolatedScope();

    public void Dispose()
    {
        _settingsScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
