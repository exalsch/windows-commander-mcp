using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class ApplicationService : IApplicationService
{
    private static readonly string[] UninstallRegistryPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public IReadOnlyList<InstalledAppInfo> ListInstalledApps(string? nameFilter, bool includeStoreApps, bool includeSystemComponents)
    {
        var apps = new List<InstalledAppInfo>();
        apps.AddRange(ReadDesktopApps(Registry.LocalMachine, nameFilter, includeSystemComponents));
        apps.AddRange(ReadDesktopApps(Registry.CurrentUser, nameFilter, includeSystemComponents));

        if (includeStoreApps)
        {
            apps.AddRange(ReadStoreAppRegistrations(nameFilter));
        }

        return apps
            .GroupBy(app => $"{app.Name}|{app.Version}|{app.InstallLocation}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AppLaunchResult LaunchApp(string identifier, string identifierType, IReadOnlyList<string>? arguments)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Application identifier must not be empty.", nameof(identifier));
        }

        var normalizedType = identifierType.ToLowerInvariant();
        var resolvedTarget = normalizedType switch
        {
            "path" => Path.GetFullPath(identifier),
            "shell_uri" => identifier,
            "shortcut_name" => ResolveShortcut(identifier),
            "aumid" => $"shell:AppsFolder\\{identifier}",
            _ => throw new ArgumentException($"Unsupported identifier type: {identifierType}")
        };

        var startInfo = new ProcessStartInfo(resolvedTarget)
        {
            UseShellExecute = true
        };

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to launch application: {identifier}");
        return new AppLaunchResult(identifier, identifierType, resolvedTarget, Started: true);
    }

    private static IEnumerable<InstalledAppInfo> ReadDesktopApps(RegistryKey root, string? nameFilter, bool includeSystemComponents)
    {
        foreach (var path in UninstallRegistryPaths)
        {
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                continue;
            }

            foreach (var subkeyName in key.GetSubKeyNames())
            {
                using var subkey = key.OpenSubKey(subkeyName);
                if (subkey is null)
                {
                    continue;
                }

                var name = subkey.GetValue("DisplayName")?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!includeSystemComponents && string.Equals(subkey.GetValue("SystemComponent")?.ToString(), "1", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(nameFilter) && !name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new InstalledAppInfo(
                    name,
                    subkey.GetValue("Publisher")?.ToString(),
                    subkey.GetValue("DisplayVersion")?.ToString(),
                    subkey.GetValue("InstallLocation")?.ToString(),
                    subkey.GetValue("UninstallString")?.ToString(),
                    null,
                    null,
                    "registry");
            }
        }
    }

    private static IEnumerable<InstalledAppInfo> ReadStoreAppRegistrations(string? nameFilter)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (key is null)
        {
            yield break;
        }

        foreach (var packageName in key.GetSubKeyNames())
        {
            if (!string.IsNullOrWhiteSpace(nameFilter) && !packageName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new InstalledAppInfo(packageName, null, null, null, null, packageName, null, "store_registry");
        }
    }

    private static string ResolveShortcut(string shortcutName)
    {
        var searchRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        var normalizedName = shortcutName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? shortcutName : $"{shortcutName}.lnk";

        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            var match = Directory.EnumerateFiles(root, normalizedName, SearchOption.AllDirectories).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        throw new FileNotFoundException($"Shortcut was not found: {shortcutName}");
    }
}
