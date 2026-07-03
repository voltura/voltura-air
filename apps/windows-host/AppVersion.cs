using System.Reflection;

namespace VolturaAir.Host;

public static class AppVersion
{
    public static string Display { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion
        .Split('+')[0] ?? "0.0.0";
}
