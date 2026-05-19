using System;
using System.Reflection;

namespace WindowsCommander.McpServer;

/// <summary>
/// Identity the server reports in its MCP <c>initialize</c> response. The
/// version is resolved from the running assembly, so a release build (which
/// stamps the version via <c>dotnet publish -p:Version=</c>) reports the real
/// tag version rather than a hardcoded constant.
/// </summary>
public static class ServerInfo
{
    public const string Name = "windows-commander-mcp";

    /// <summary>Version reported to MCP clients, resolved once at startup.</summary>
    public static string Version { get; } = ResolveVersion();

    static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return CleanVersion(informational, assembly.GetName().Version);
    }

    /// <summary>
    /// Normalises a raw version string. Prefers the SemVer-style informational
    /// version, dropping any <c>+build</c> metadata the SDK appends; falls back
    /// to the three-part assembly version when no informational version is set.
    /// </summary>
    public static string CleanVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plus = informationalVersion.IndexOf('+');
            return plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        }

        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }
}
