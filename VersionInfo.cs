using System.Reflection;

namespace Soundpad;

internal static class VersionInfo
{
    public static string Current => typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "unknown";
}
