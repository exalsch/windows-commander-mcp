namespace WindowsCommander.Core.Models;

public sealed record DisplayMetric(
    string MonitorId,
    string DeviceName,
    string Resolution,
    RectBounds VirtualCoordinates,
    int RefreshRate,
    double DpiScalingFactor);

public sealed record WindowScreenInfo(
    long WindowHandle,
    string WindowTitle,
    RectBounds WindowBounds,
    ScreenDetails Screen,
    bool IsFullyOnScreen,
    double VisibleAreaRatio);

public sealed record NotificationResult(
    string Title,
    string Message,
    DateTimeOffset ShownAt,
    bool Delivered);

public sealed record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string ServiceType,
    string StartType,
    string? ImagePath,
    string? AccountName);

public sealed record RegistryValueInfo(
    string Hive,
    string KeyPath,
    string Name,
    string ValueKind,
    object? Value);

public sealed record InstalledAppInfo(
    string Name,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    string? UninstallCommand,
    string? PackageFamilyName,
    string? AppUserModelId,
    string Source);

public sealed record AppLaunchResult(
    string Identifier,
    string IdentifierType,
    string ResolvedTarget,
    bool Started);
