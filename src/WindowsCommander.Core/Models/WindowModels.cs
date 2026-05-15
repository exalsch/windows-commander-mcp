namespace WindowsCommander.Core.Models;

public sealed record WindowSummary(
    nint HWND,
    string Title,
    int OwningPID,
    bool IsMinimized,
    RectBounds BoundingRect);

public sealed record WindowDetails(
    nint HWND,
    string Title,
    string ClassName,
    int OwningPID,
    string ProcessName,
    bool IsVisible,
    bool IsMinimized,
    RectBounds BoundingRect);
