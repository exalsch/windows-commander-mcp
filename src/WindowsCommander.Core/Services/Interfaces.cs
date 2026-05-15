using WindowsCommander.Core.Models;

namespace WindowsCommander.Core.Services;

public interface IProcessService
{
    IReadOnlyList<ProcessSummary> ListProcesses(string? filterName, bool sortByMemory);

    ProcessDetails GetProcessDetails(int pid);
}

public interface IWindowService
{
    IReadOnlyList<WindowSummary> ListWindows();

    IReadOnlyList<WindowDetails> FindWindows(string? titleContains, string? className, string? processName, int? pid, bool visibleOnly);
}

public interface IScreenService
{
    IReadOnlyList<ScreenDetails> GetScreenDetails();

    ScreenAtPoint GetScreenAtPoint(int x, int y);
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
