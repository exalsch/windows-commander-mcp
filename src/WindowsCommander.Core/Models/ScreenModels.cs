namespace WindowsCommander.Core.Models;

public sealed record ScreenDetails(
    string MonitorId,
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    RectBounds Bounds,
    RectBounds WorkingArea,
    double DpiScale,
    string Orientation,
    string Resolution,
    int ColorDepth,
    int RefreshRate,
    string AdapterName,
    bool IsActive);

public sealed record ScreenAtPoint(
    ScreenDetails Screen,
    int LocalX,
    int LocalY,
    bool IsInsideWorkingArea);
