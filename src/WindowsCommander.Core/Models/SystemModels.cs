namespace WindowsCommander.Core.Models;

public sealed record SystemInfo(
    string OSVersion,
    string MachineName,
    string CurrentUser,
    string IntegrityLevel,
    string Architecture,
    TimeSpan Uptime,
    string BatteryStatus,
    string Locale,
    string TimeZone,
    bool ActiveSession);
