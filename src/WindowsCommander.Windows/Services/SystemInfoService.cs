using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class SystemInfoService : ISystemInfoService
{
    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo(
            Environment.OSVersion.VersionString,
            Environment.MachineName,
            Environment.UserName,
            "Unknown",
            RuntimeInformation.ProcessArchitecture.ToString(),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            SystemInformation.PowerStatus.BatteryChargeStatus.ToString(),
            CultureInfo.CurrentCulture.Name,
            TimeZoneInfo.Local.Id,
            Environment.UserInteractive);
    }
}
