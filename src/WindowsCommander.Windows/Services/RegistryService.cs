using Microsoft.Win32;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class RegistryService : IRegistryService
{
    public IReadOnlyList<RegistryValueInfo> ReadRegistry(string hive, string keyPath, string? valueName)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            throw new ArgumentException("Registry key path must not be empty.", nameof(keyPath));
        }

        using var root = ResolveHive(hive);
        using var key = root.OpenSubKey(keyPath, writable: false) ?? throw new InvalidOperationException($"Registry key was not found: {hive}\\{keyPath}");

        if (!string.IsNullOrWhiteSpace(valueName))
        {
            return new[] { ToInfo(hive, keyPath, valueName, key) };
        }

        return key.GetValueNames()
            .Select(name => ToInfo(hive, keyPath, name, key))
            .ToArray();
    }

    private static RegistryKey ResolveHive(string hive)
    {
        return hive.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unsupported registry hive: {hive}")
        };
    }

    private static RegistryValueInfo ToInfo(string hive, string keyPath, string name, RegistryKey key)
    {
        return new RegistryValueInfo(
            hive,
            keyPath,
            string.IsNullOrEmpty(name) ? "(Default)" : name,
            key.GetValueKind(name).ToString(),
            key.GetValue(name));
    }
}
