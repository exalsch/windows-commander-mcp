using WindowsCommander.Core.Models;

namespace WindowsCommander.Core.Services;

public interface IProcessService
{
    IReadOnlyList<ProcessSummary> ListProcesses(string? filterName, bool sortByMemory);

    ProcessDetails GetProcessDetails(int pid);

    ProcessActionResult ManageProcess(int pid, string action);
}

public interface IWindowService
{
    IReadOnlyList<WindowSummary> ListWindows();

    IReadOnlyList<WindowDetails> FindWindows(string? titleContains, string? className, string? processName, int? pid, bool visibleOnly, bool processNameExact = false);

    WindowActionResult FocusWindow(long windowHandle);

    WindowActionResult MoveResizeWindow(long windowHandle, int? x, int? y, int? width, int? height);

    WindowActionResult SetWindowState(long windowHandle, string state);

    Task<WindowDetails> WaitForWindowAsync(string? titleContains, string? className, string? processName, int? pid, int timeoutMs, CancellationToken cancellationToken, bool processNameExact = false);

    IReadOnlyList<WindowDetails> EnumerateChildWindows(long windowHandle);
}

public interface IScreenService
{
    IReadOnlyList<ScreenDetails> GetScreenDetails();

    ScreenAtPoint GetScreenAtPoint(int x, int y);

    IReadOnlyList<DisplayMetric> GetDisplayMetrics();

    WindowScreenInfo GetWindowScreenInfo(long windowHandle);

    NotificationResult ShowNotification(string title, string message, int? timeoutMs);
}

public interface ISystemInfoService
{
    SystemInfo GetSystemInfo();
}

public interface IExecutionService
{
    Task<CommandExecutionResult> ExecutePowerShellAsync(
        string command,
        string? workingDirectory,
        int? timeoutMs,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken);

    Task<ProcessStartResult> ExecuteProcessAsync(
        string executablePath,
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        int? timeoutMs,
        bool waitForExit,
        CancellationToken cancellationToken);
}

public interface IEnvironmentService
{
    string? GetEnvironmentVariable(string name, string scope);

    void SetEnvironmentVariable(string name, string? value, string scope);
}

public interface IClipboardService
{
    object? Access(string action, string? content, string? format);
}

public interface IFileSystemService
{
    IReadOnlyList<DirectoryEntry> ListDirectory(string path, bool recursive, bool includeHidden, string? pattern);

    Task<FileReadResult> ReadFileAsync(string path, string? encoding, int? maxBytes, bool asBase64, CancellationToken cancellationToken);

    Task<FileWriteResult> WriteFileAsync(string path, string content, string? encoding, bool overwrite, bool createDirectories, CancellationToken cancellationToken);

    Task<PathOperationResult> CopyMoveDeletePathAsync(
        string action,
        string sourcePath,
        string? destinationPath,
        bool recursive,
        bool overwrite,
        CancellationToken cancellationToken);

    Task<FileProperties> GetFilePropertiesAsync(string path, string? hashAlgorithm, CancellationToken cancellationToken);

    IReadOnlyList<FileSearchResult> SearchFiles(
        IReadOnlyList<string> roots,
        string? namePattern,
        string? contentQuery,
        bool includeHidden,
        int? maxResults);
}

public interface IShellService
{
    void OpenPath(string pathOrUri, string? verb, IReadOnlyList<string>? arguments);

    void ShowInExplorer(string path);
}

public interface IWindowsServiceDiscoveryService
{
    IReadOnlyList<WindowsServiceInfo> ListServices(string? nameFilter, string? statusFilter);
}

public interface IRegistryService
{
    IReadOnlyList<RegistryValueInfo> ReadRegistry(string hive, string keyPath, string? valueName);
}

public interface IApplicationService
{
    IReadOnlyList<InstalledAppInfo> ListInstalledApps(string? nameFilter, bool includeStoreApps, bool includeSystemComponents);

    AppLaunchResult LaunchApp(string identifier, string identifierType, IReadOnlyList<string>? arguments);
}

public interface IInputService
{
    InputActionResult MouseAction(string action, string? button, int? x, int? y, long? targetWindowHandle);

    Task<InputActionResult> TypeTextAsync(string text, int? speedMs, CancellationToken cancellationToken);

    InputActionResult SendHotkey(IReadOnlyList<string> modifiers, string key);

    Task<InputActionResult> KeyboardActionAsync(string action, string key, int? repeat, int? delayMs, CancellationToken cancellationToken);

    InputActionResult MouseWheel(string direction, int amount, long? targetWindowHandle, int? x, int? y);

    CursorPositionInfo GetCursorPosition();

    Task<InputActionResult> SetCursorPositionAsync(int x, int y, int? durationMs, CancellationToken cancellationToken);

    Task<InputActionResult> InputSequenceAsync(IReadOnlyList<InputSequenceStep> steps, bool abortOnError, CancellationToken cancellationToken);
}

public sealed record InputSequenceStep(string Type, string? Action, string? Text, string? Key, IReadOnlyList<string>? Modifiers, int? X, int? Y, int? DelayMs);

public interface IVisionService
{
    ScreenCaptureResult CaptureScreen(string target, long? windowHandle, int? maxDimension);

    ScreenCaptureResult CaptureScreenRegion(int x, int y, int width, int height, string? monitorId, int? maxDimension);

    Task<OcrResult> OcrScreenAsync(string target, long? windowHandle, RectBounds? region);

    VisualDetectionResult DetectVisualElements(string target, long? windowHandle, RectBounds? region, IReadOnlyList<string>? elementTypes);
}

public interface IUiAutomationService
{
    UiTreeResult ReadUiTree(long windowHandle, int maxDepth = 5);

    IReadOnlyList<UiElementInfo> FindUiElement(long windowHandle, string? nameContains, string? automationId, string? controlType, string? className, bool enabledOnly, int maxDepth = 5);

    UiActionResult InvokeUiElement(string elementRef, string action);

    UiActionResult SetUiValue(string elementRef, string value);

    UiElementDetails GetUiElementDetails(string elementRef);
}

public interface IControlIndicatorService
{
    ControlIndicatorStatus ShowControlIndicator(string message, RectBounds? bounds);

    ControlIndicatorStatus HideControlIndicator();

    ControlIndicatorStatus ConfigureControlIndicators(ControlIndicatorConfig config);

    ControlIndicatorStatus GetControlIndicatorStatus();

    /// <summary>
    /// Drives the screen-edge activity glow. Called automatically whenever a
    /// computer-use tool runs so the user has a visible cue that automation is
    /// driving their desktop. The glow shows a bright pulse for the action,
    /// then settles to a faint persistent border (the session is still live)
    /// and only fully hides after a longer idle. <paramref name="elevated"/>
    /// switches the glow to a warning colour for high-risk actions.
    /// </summary>
    void SignalActivity(string message, bool elevated);

    ConfirmationResult RequestUserConfirmation(string title, string message, string riskLevel, int? timeoutMs);
}
