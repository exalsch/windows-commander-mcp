using System.ServiceProcess;
using Microsoft.Win32;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class WindowsServiceDiscoveryService : IWindowsServiceDiscoveryService
{
    public IReadOnlyList<WindowsServiceInfo> ListServices(string? nameFilter, string? statusFilter)
    {
        return ServiceController.GetServices()
            .Where(service => Matches(service, nameFilter, statusFilter))
            .Select(ToInfo)
            .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool Matches(ServiceController service, string? nameFilter, string? statusFilter)
    {
        var nameMatches = string.IsNullOrWhiteSpace(nameFilter)
            || service.ServiceName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
            || service.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);
        var statusMatches = string.IsNullOrWhiteSpace(statusFilter)
            || string.Equals(service.Status.ToString(), statusFilter, StringComparison.OrdinalIgnoreCase);

        return nameMatches && statusMatches;
    }

    private static WindowsServiceInfo ToInfo(ServiceController service)
    {
        var imagePath = ReadServiceRegistryValue(service.ServiceName, "ImagePath");
        var accountName = ReadServiceRegistryValue(service.ServiceName, "ObjectName");
        var startType = ReadServiceRegistryValue(service.ServiceName, "Start");

        return new WindowsServiceInfo(
            service.ServiceName,
            service.DisplayName,
            service.Status.ToString(),
            service.ServiceType.ToString(),
            MapStartType(startType),
            imagePath,
            accountName);
    }

    private static string? ReadServiceRegistryValue(string serviceName, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key?.GetValue(valueName)?.ToString();
    }

    private static string MapStartType(string? startType)
    {
        return startType switch
        {
            "0" => "Boot",
            "1" => "System",
            "2" => "Automatic",
            "3" => "Manual",
            "4" => "Disabled",
            _ => startType ?? string.Empty
        };
    }
}
