using System.Text.Json;
using WindowsCommander.Core.Safety;
using WindowsCommander.Core.Services;

namespace WindowsCommander.McpServer.Mcp;

public sealed class ToolDispatcher
{
    private readonly IProcessService processService;
    private readonly IWindowService windowService;
    private readonly IScreenService screenService;
    private readonly ISystemInfoService systemInfoService;
    private readonly IExecutionService executionService;
    private readonly IEnvironmentService environmentService;
    private readonly IClipboardService clipboardService;
    private readonly IFileSystemService fileSystemService;
    private readonly IShellService shellService;
    private readonly IAuditLog auditLog;

    public ToolDispatcher(
        IProcessService processService,
        IWindowService windowService,
        IScreenService screenService,
        ISystemInfoService systemInfoService,
        IExecutionService executionService,
        IEnvironmentService environmentService,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IShellService shellService,
        IAuditLog auditLog)
    {
        this.processService = processService;
        this.windowService = windowService;
        this.screenService = screenService;
        this.systemInfoService = systemInfoService;
        this.executionService = executionService;
        this.environmentService = environmentService;
        this.clipboardService = clipboardService;
        this.fileSystemService = fileSystemService;
        this.shellService = shellService;
        this.auditLog = auditLog;
    }

    public object ListTools()
    {
        return new
        {
            tools = new[]
            {
                Tool("list_processes", "Retrieves a list of currently running processes."),
                Tool("get_process_details", "Fetches deep system information for a specific process."),
                Tool("list_windows", "Returns a hierarchical list of active, visible windows."),
                Tool("find_window", "Locates windows by title, class name, executable name, process ID, or visibility state."),
                Tool("get_screen_details", "Retrieves detailed information about every attached screen."),
                Tool("get_screen_at_point", "Resolves which screen contains a specific virtual-screen coordinate."),
                Tool("get_system_info", "Retrieves host-level metadata useful for automation decisions."),
                Tool("execute_powershell", "Runs a PowerShell script or command silently."),
                Tool("execute_process", "Starts a native executable with explicit arguments and controlled output capture."),
                Tool("clipboard_access", "Interacts with the system clipboard."),
                Tool("get_environment_variable", "Reads environment variables from process, user, or machine scope."),
                Tool("set_environment_variable", "Sets or removes environment variables from process, user, or machine scope."),
                Tool("list_directory", "Lists files and directories with Windows metadata."),
                Tool("read_file", "Reads text or binary file content with encoding detection."),
                Tool("write_file", "Writes file content with explicit overwrite control."),
                Tool("copy_move_delete_path", "Copies, moves, deletes, or sends files and directories to the recycle bin."),
                Tool("get_file_properties", "Retrieves file metadata, version info, hashes, and security descriptor summary."),
                Tool("open_path", "Opens a file, folder, URI, or shell verb using the Windows shell."),
                Tool("show_in_explorer", "Opens File Explorer with a file or folder selected."),
                Tool("search_files", "Searches file names and optional file content across one or more roots."),
                Tool("get_operation_history", "Returns a bounded audit log of recent tool executions.")
            }
        };
    }

    public async Task<object> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var operationId = Guid.NewGuid().ToString("N");
        var args = ToDictionary(arguments);

        try
        {
            var result = await DispatchAsync(name, arguments, cancellationToken);
            auditLog.Record(new AuditEntry(operationId, name, startedAt, DateTimeOffset.UtcNow, "success", args, null));

            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, JsonOptions.Default)
                    }
                }
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            auditLog.Record(new AuditEntry(operationId, name, startedAt, DateTimeOffset.UtcNow, "error", args, exception.Message));
            throw;
        }
    }

    private async Task<object> DispatchAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        return name switch
        {
            "list_processes" => processService.ListProcesses(GetString(arguments, "filter_name"), GetBool(arguments, "sort_by_memory") ?? false),
            "get_process_details" => processService.GetProcessDetails(GetRequiredInt(arguments, "pid")),
            "list_windows" => windowService.ListWindows(),
            "find_window" => windowService.FindWindows(
                GetString(arguments, "title_contains"),
                GetString(arguments, "class_name"),
                GetString(arguments, "process_name"),
                GetInt(arguments, "pid"),
                GetBool(arguments, "visible_only") ?? false),
            "get_screen_details" => screenService.GetScreenDetails(),
            "get_screen_at_point" => screenService.GetScreenAtPoint(GetRequiredInt(arguments, "x"), GetRequiredInt(arguments, "y")),
            "get_system_info" => systemInfoService.GetSystemInfo(),
            "execute_powershell" => await executionService.ExecutePowerShellAsync(
                GetRequiredString(arguments, "command"),
                GetString(arguments, "working_directory"),
                GetInt(arguments, "timeout_ms"),
                GetStringDictionary(arguments, "environment"),
                cancellationToken),
            "execute_process" => await executionService.ExecuteProcessAsync(
                GetRequiredString(arguments, "executable_path"),
                GetStringArray(arguments, "arguments"),
                GetString(arguments, "working_directory"),
                GetInt(arguments, "timeout_ms"),
                GetBool(arguments, "wait_for_exit") ?? false,
                cancellationToken),
            "clipboard_access" => clipboardService.Access(
                GetRequiredString(arguments, "action"),
                GetString(arguments, "content"),
                GetString(arguments, "format")) ?? new { value = (object?)null },
            "get_environment_variable" => new
            {
                value = environmentService.GetEnvironmentVariable(
                    GetRequiredString(arguments, "name"),
                    GetRequiredString(arguments, "scope"))
            },
            "set_environment_variable" => SetEnvironmentVariable(arguments),
            "list_directory" => fileSystemService.ListDirectory(
                GetRequiredString(arguments, "path"),
                GetBool(arguments, "recursive") ?? false,
                GetBool(arguments, "include_hidden") ?? false,
                GetString(arguments, "pattern")),
            "read_file" => await fileSystemService.ReadFileAsync(
                GetRequiredString(arguments, "path"),
                GetString(arguments, "encoding"),
                GetInt(arguments, "max_bytes"),
                GetBool(arguments, "as_base64") ?? false,
                cancellationToken),
            "write_file" => await fileSystemService.WriteFileAsync(
                GetRequiredString(arguments, "path"),
                GetRequiredString(arguments, "content"),
                GetString(arguments, "encoding"),
                GetBool(arguments, "overwrite") ?? false,
                GetBool(arguments, "create_directories") ?? false,
                cancellationToken),
            "copy_move_delete_path" => await fileSystemService.CopyMoveDeletePathAsync(
                GetRequiredString(arguments, "action"),
                GetRequiredString(arguments, "source_path"),
                GetString(arguments, "destination_path"),
                GetBool(arguments, "recursive") ?? false,
                GetBool(arguments, "overwrite") ?? false,
                cancellationToken),
            "get_file_properties" => await fileSystemService.GetFilePropertiesAsync(
                GetRequiredString(arguments, "path"),
                GetString(arguments, "hash_algorithm"),
                cancellationToken),
            "open_path" => OpenPath(arguments),
            "show_in_explorer" => ShowInExplorer(arguments),
            "search_files" => fileSystemService.SearchFiles(
                GetRequiredStringArray(arguments, "roots"),
                GetString(arguments, "name_pattern"),
                GetString(arguments, "content_query"),
                GetBool(arguments, "include_hidden") ?? false,
                GetInt(arguments, "max_results")),
            "get_operation_history" => auditLog.GetRecent(GetInt(arguments, "limit") ?? 50, GetBool(arguments, "include_sensitive_arguments") ?? false),
            _ => throw new ArgumentException($"Unknown tool: {name}")
        };
    }

    private object SetEnvironmentVariable(JsonElement? arguments)
    {
        environmentService.SetEnvironmentVariable(
            GetRequiredString(arguments, "name"),
            GetString(arguments, "value"),
            GetRequiredString(arguments, "scope"));

        return new { updated = true };
    }

    private object OpenPath(JsonElement? arguments)
    {
        shellService.OpenPath(
            GetRequiredString(arguments, "path_or_uri"),
            GetString(arguments, "verb"),
            GetStringArray(arguments, "arguments"));

        return new { opened = true };
    }

    private object ShowInExplorer(JsonElement? arguments)
    {
        shellService.ShowInExplorer(GetRequiredString(arguments, "path"));
        return new { opened = true };
    }

    private static McpTool Tool(string name, string description)
    {
        return new McpTool(name, description, new { type = "object", properties = new { } });
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement? arguments)
    {
        if (arguments is null || arguments.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.Value.GetRawText(), JsonOptions.Default)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement? arguments, string name)
    {
        return TryGetProperty(arguments, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement? arguments, string name)
    {
        return TryGetProperty(arguments, name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static int GetRequiredInt(JsonElement? arguments, string name)
    {
        return GetInt(arguments, name) ?? throw new ArgumentException($"Missing or invalid integer argument: {name}");
    }

    private static string GetRequiredString(JsonElement? arguments, string name)
    {
        return GetString(arguments, name) ?? throw new ArgumentException($"Missing or invalid string argument: {name}");
    }

    private static bool? GetBool(JsonElement? arguments, string name)
    {
        return TryGetProperty(arguments, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static IReadOnlyList<string>? GetStringArray(JsonElement? arguments, string name)
    {
        if (!TryGetProperty(arguments, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Argument must be an array of strings: {name}");
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : throw new ArgumentException($"Argument contains a non-string value: {name}"))
            .ToArray();
    }

    private static IReadOnlyList<string> GetRequiredStringArray(JsonElement? arguments, string name)
    {
        return GetStringArray(arguments, name) ?? throw new ArgumentException($"Missing or invalid string array argument: {name}");
    }

    private static IReadOnlyDictionary<string, string>? GetStringDictionary(JsonElement? arguments, string name)
    {
        if (!TryGetProperty(arguments, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Argument must be an object with string values: {name}");
        }

        return value.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : throw new ArgumentException($"Environment value must be a string: {property.Name}"),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement? arguments, string name, out JsonElement value)
    {
        value = default;
        return arguments is not null
            && arguments.Value.ValueKind == JsonValueKind.Object
            && arguments.Value.TryGetProperty(name, out value);
    }
}
