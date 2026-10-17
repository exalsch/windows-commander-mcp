namespace WindowsCommander.Core.Models;

public sealed record ProcessActionResult(int PID, string Action, bool Completed);

public sealed record WindowActionResult(long WindowHandle, string Action, bool Completed, RectBounds? Bounds, string? State);

public sealed record CursorPositionInfo(int X, int Y, string MonitorId, bool IsOverWindow, long? WindowHandle);

public sealed record InputActionResult(string Action, bool Completed, int StepsCompleted);

public sealed record ScreenCaptureResult(string PngBase64, RectBounds Region, string? MonitorId, DateTimeOffset CapturedAt);

public sealed record UiElementInfo(
    string ElementRef,
    string Name,
    string AutomationId,
    string ControlType,
    string ClassName,
    RectBounds Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    string? Value);

public sealed record UiTreeResult(
    IReadOnlyList<UiElementInfo> Elements,
    bool Truncated,
    int MaxDepth,
    int ElementCount);

public sealed record UiElementDetails(
    UiElementInfo Element,
    IReadOnlyList<string> SupportedActions,
    string? ParentName,
    IReadOnlyList<UiElementInfo> Children);

public sealed record UiActionResult(string ElementRef, string Action, bool Completed);

public sealed record OcrTextBlock(string Text, double Confidence, RectBounds Bounds);

public sealed record OcrResult(IReadOnlyList<OcrTextBlock> Blocks, string CombinedText, RectBounds Region, DateTimeOffset CapturedAt);

public sealed record VisualElementCandidate(string ElementType, string Label, double Confidence, RectBounds Bounds);

public sealed record VisualDetectionResult(IReadOnlyList<VisualElementCandidate> Candidates, RectBounds Region, DateTimeOffset CapturedAt);

public sealed record ControlIndicatorConfig(bool VisualEnabled, bool AudioEnabled, string BorderColor, int BorderThickness, int AudioFrequencyHz, int AudioDurationMs);

public sealed record ControlIndicatorStatus(bool IsVisible, string? Message, RectBounds? Bounds, ControlIndicatorConfig Config);

public sealed record ConfirmationResult(string Title, string Message, string RiskLevel, string Decision, DateTimeOffset DecidedAt);

// The UI Automation element that currently has keyboard focus, plus the
// top-level window that hosts it, so a caller can check where keystrokes will
// land before typing.
public sealed record FocusedUiElementInfo(UiElementInfo Element, int ProcessId, string ProcessName, long? TopLevelWindowHandle, string? TopLevelWindowTitle);

// Outcome of ask_user. Decision is "answered", "cancelled" or "timeout";
// Choice is the clicked option (null when there were no options or no answer)
// and Text the typed answer (null unless a text field was shown).
public sealed record UserPromptResult(string Title, string Message, string Decision, string? Choice, string? Text, DateTimeOffset DecidedAt);
