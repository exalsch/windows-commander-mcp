using System.Text.Json;
using WindowsCommander.Core.Models;
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
    private readonly IWindowsServiceDiscoveryService windowsServiceDiscoveryService;
    private readonly IRegistryService registryService;
    private readonly IApplicationService applicationService;
    private readonly IInputService inputService;
    private readonly IVisionService visionService;
    private readonly IUiAutomationService uiAutomationService;
    private readonly IControlIndicatorService controlIndicatorService;
    private readonly IAuditLog auditLog;
    private readonly IRiskPolicyService riskPolicy;
    // When true, high-risk tools are gated behind a local confirmation dialog.
    // Disabled (unattended mode) for automated harness/CI runs.
    private readonly bool requireConfirmation;
    private readonly ComputerUseNotifier notifier = new();

    // Tools that actively drive the desktop (mouse, keyboard, windows, UI,
    // screen capture). A call to one of these plays the computer-use chime.
    private static readonly HashSet<string> ComputerUseTools = new(StringComparer.Ordinal)
    {
        "manage_process",
        "focus_window", "move_resize_window", "set_window_state",
        "mouse_action", "type_text", "send_hotkey", "keyboard_action", "mouse_wheel",
        "set_cursor_position", "input_sequence",
        "capture_screen", "capture_screen_region",
        "invoke_ui_element", "set_ui_value",
        "launch_app", "open_path", "show_in_explorer"
    };

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
        IWindowsServiceDiscoveryService windowsServiceDiscoveryService,
        IRegistryService registryService,
        IApplicationService applicationService,
        IInputService inputService,
        IVisionService visionService,
        IUiAutomationService uiAutomationService,
        IControlIndicatorService controlIndicatorService,
        IAuditLog auditLog,
        IRiskPolicyService riskPolicy,
        bool requireConfirmation)
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
        this.windowsServiceDiscoveryService = windowsServiceDiscoveryService;
        this.registryService = registryService;
        this.applicationService = applicationService;
        this.inputService = inputService;
        this.visionService = visionService;
        this.uiAutomationService = uiAutomationService;
        this.controlIndicatorService = controlIndicatorService;
        this.auditLog = auditLog;
        this.riskPolicy = riskPolicy;
        this.requireConfirmation = requireConfirmation;
    }

    public object ListTools()
    {
        return new
        {
            tools = new[]
            {
                Tool("list_processes", "Retrieves a list of currently running processes.",
                    Str("filter_name", "Case-insensitive substring to match against process names."),
                    Bool("sort_by_memory", "Sort by working-set memory descending when true.")),
                Tool("get_process_details", "Fetches deep system information for a specific process.",
                    Int("pid", "Process id to inspect.", required: true)),
                Tool("manage_process", "Terminates or closes a process.",
                    Int("pid", "Process id to act on.", required: true),
                    Str("action", "How to stop the process.", true, "terminate", "kill", "kill_tree", "close_main_window")),
                Tool("list_windows", "Returns a hierarchical list of active, visible windows."),
                Tool("find_window", "Locates windows by title, class name, executable name, process ID, or visibility state.",
                    Str("title_contains", "Case-insensitive substring to match against window titles."),
                    Str("class_name", "Exact Win32 window class name to match."),
                    Str("process_name", "Owning executable name to match (with or without .exe). Matched as a case-insensitive substring unless process_name_exact is true."),
                    Bool("process_name_exact", "Match process_name as a whole, case-insensitively, instead of a substring (default false). Set true to avoid e.g. 'notepad' also matching 'notepad++'."),
                    Int("pid", "Owning process id to match."),
                    Bool("visible_only", "Restrict results to visible windows when true.")),
                Tool("focus_window", "Brings a window to the foreground.",
                    Int("window_handle", "Native window handle (HWND) to focus.", required: true)),
                Tool("move_resize_window", "Moves and resizes a window.",
                    Int("window_handle", "Native window handle (HWND) to move.", required: true),
                    Int("x", "New left position in virtual-screen pixels."),
                    Int("y", "New top position in virtual-screen pixels."),
                    Int("width", "New width in pixels."),
                    Int("height", "New height in pixels.")),
                Tool("set_window_state", "Changes a window state.",
                    Int("window_handle", "Native window handle (HWND) to update.", required: true),
                    Str("state", "Target window state.", true, "hide", "normal", "restore", "minimize", "maximize")),
                Tool("wait_for_window", "Waits until a matching window appears.",
                    Str("title_contains", "Case-insensitive substring to match against window titles."),
                    Str("class_name", "Exact Win32 window class name to match."),
                    Str("process_name", "Owning executable name to match. Matched as a case-insensitive substring unless process_name_exact is true."),
                    Bool("process_name_exact", "Match process_name as a whole, case-insensitively, instead of a substring (default false). Set true to avoid e.g. 'notepad' also matching 'notepad++'."),
                    Int("pid", "Owning process id to match."),
                    Int("timeout_ms", "Maximum time to wait in milliseconds (default 30000).")),
                Tool("enumerate_child_windows", "Enumerates child windows for a parent window.",
                    Int("window_handle", "Parent native window handle (HWND).", required: true)),
                Tool("mouse_action", "Performs mouse movements and clicks.",
                    Str("action", "Mouse action to perform.", true, "move", "click", "double_click", "drag"),
                    Str("button", "Mouse button to use.", false, "left", "right", "middle"),
                    Int("x", "Target X coordinate in virtual-screen pixels."),
                    Int("y", "Target Y coordinate in virtual-screen pixels."),
                    Int("target_hwnd", "Optional window handle to resolve coordinates against.")),
                Tool("type_text", "Injects a stream of keystrokes into the focused window.",
                    Str("text", "Text to type into the currently focused control.", required: true),
                    Int("speed_ms", "Delay between keystrokes in milliseconds.")),
                Tool("send_hotkey", "Executes keyboard shortcuts.",
                    StrArray("modifiers", "Modifier keys held down, e.g. ctrl, alt, shift, win.", required: true),
                    Str("key", "Primary key pressed while the modifiers are held.", required: true)),
                Tool("keyboard_action", "Sends low-level key press, release, or tap events.",
                    Str("action", "Key event to send.", true, "press", "release", "tap"),
                    Str("key", "Key name to act on.", required: true),
                    Int("repeat", "Number of times to repeat the action."),
                    Int("delay_ms", "Delay between repeats in milliseconds.")),
                Tool("mouse_wheel", "Scrolls at the current cursor location or target.",
                    Str("direction", "Scroll direction.", true, "up", "down", "left", "right"),
                    Int("amount", "Number of scroll increments.", required: true),
                    Int("target_hwnd", "Optional window handle to scroll over."),
                    Int("x", "Optional X coordinate to scroll over."),
                    Int("y", "Optional Y coordinate to scroll over.")),
                Tool("get_cursor_position", "Returns the current cursor position."),
                Tool("set_cursor_position", "Moves the cursor to a virtual-screen coordinate.",
                    Int("x", "Target X coordinate in virtual-screen pixels.", required: true),
                    Int("y", "Target Y coordinate in virtual-screen pixels.", required: true),
                    Int("duration_ms", "Animation duration for the move in milliseconds.")),
                Tool("input_sequence", "Executes a sequence of input actions.",
                    StepsParam("steps", "Ordered input steps to execute.", required: true),
                    Bool("abort_on_error", "Stop the sequence on the first failing step (default true).")),
                Tool("capture_screen", "Captures pixels from the screen and returns a PNG image.",
                    Str("target", "What to capture: 'full_screen', 'active_window', or a numeric window handle.", required: true),
                    Int("hwnd", "Explicit window handle to capture (overrides target)."),
                    Int("max_dimension", "Cap the longest image side in pixels; the capture is downscaled to fit (default 1400).")),
                Tool("capture_screen_region", "Captures a rectangular screen region as a PNG image.",
                    Int("x", "Region left position in virtual-screen pixels.", required: true),
                    Int("y", "Region top position in virtual-screen pixels.", required: true),
                    Int("width", "Region width in pixels.", required: true),
                    Int("height", "Region height in pixels.", required: true),
                    Str("monitor_id", "Optional monitor identifier the region belongs to."),
                    Int("max_dimension", "Cap the longest image side in pixels (default 1400).")),
                Tool("read_ui_tree", "Reads the UI Automation tree for a window. Returns a result object with the flattened 'elements' list plus 'truncated' (true when elements deeper than max_depth were dropped), 'maxDepth', and 'elementCount'.",
                    Int("hwnd", "Native window handle (HWND) whose UI tree to read.", required: true),
                    Int("max_depth", "Maximum tree depth to descend, clamped to 1..20 (default 5). Increase if the result reports truncated=true.")),
                Tool("find_ui_element", "Searches UI Automation elements.",
                    Int("hwnd", "Native window handle (HWND) to search within.", required: true),
                    Int("max_depth", "Maximum tree depth to search, clamped to 1..20 (default 5). Increase if a deeply nested element is not found."),
                    Str("name_contains", "Case-insensitive substring to match against element names."),
                    Str("automation_id", "Exact AutomationId to match."),
                    Str("control_type", "UI Automation control type to match (matched exactly). Note: most multi-line text editors, including Notepad, expose their editing surface as 'Document', not 'Edit'.", false,
                        "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem",
                        "List", "Menu", "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider",
                        "Spinner", "StatusBar", "Tab", "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem",
                        "Custom", "Group", "Thumb", "DataGrid", "DataItem", "Document", "SplitButton", "Window",
                        "Pane", "Header", "HeaderItem", "Table", "TitleBar", "Separator", "SemanticZoom", "AppBar"),
                    Str("class_name", "Element class name to match."),
                    Bool("enabled_only", "Restrict results to enabled elements when true.")),
                Tool("invoke_ui_element", "Invokes a UI Automation element action.",
                    Str("element_ref", "Element reference returned by find_ui_element or read_ui_tree.", required: true),
                    Str("action", "Action to invoke on the element.", true, "invoke", "select", "expand", "collapse", "toggle", "focus")),
                Tool("set_ui_value", "Sets an editable UI Automation value.",
                    Str("element_ref", "Element reference for an editable control.", required: true),
                    Str("value", "New value to set.", required: true)),
                Tool("get_ui_element_details", "Gets detailed UI Automation element information.",
                    Str("element_ref", "Element reference to inspect.", required: true)),
                Tool("ocr_screen", "Extracts local visible text metadata from a screen/window region.",
                    Str("target", "What to read: 'full_screen', 'active_window', or a numeric window handle.", required: true),
                    Int("hwnd", "Explicit window handle to read."),
                    RectParam("region", "Optional sub-region to limit the read.")),
                Tool("detect_visual_elements", "Detects local visual candidates from windows/UI metadata.",
                    Str("target", "What to scan: 'full_screen', 'active_window', or a numeric window handle.", required: true),
                    Int("hwnd", "Explicit window handle to scan."),
                    RectParam("region", "Optional sub-region to limit the scan."),
                    StrArray("element_types", "Element categories to detect, e.g. window.")),
                Tool("get_screen_details", "Retrieves detailed information about every attached screen."),
                Tool("get_screen_at_point", "Resolves which screen contains a specific virtual-screen coordinate.",
                    Int("x", "X coordinate in virtual-screen pixels.", required: true),
                    Int("y", "Y coordinate in virtual-screen pixels.", required: true)),
                Tool("get_system_info", "Retrieves host-level metadata useful for automation decisions."),
                Tool("execute_powershell", "Runs a PowerShell script or command silently.",
                    Str("command", "PowerShell script or command text to run.", required: true),
                    Str("working_directory", "Working directory for the invocation."),
                    Int("timeout_ms", "Maximum execution time in milliseconds."),
                    MapParam("environment", "Extra environment variables as a string-to-string map.")),
                Tool("execute_process", "Starts a native executable with explicit arguments and controlled output capture.",
                    Str("executable_path", "Full path to the executable to start.", required: true),
                    StrArray("arguments", "Command-line arguments passed individually."),
                    Str("working_directory", "Working directory for the process."),
                    Int("timeout_ms", "Maximum execution time in milliseconds."),
                    Bool("wait_for_exit", "Wait for the process to exit and capture output when true.")),
                Tool("clipboard_access", "Interacts with the system clipboard.",
                    Str("action", "Clipboard operation to perform.", true, "read", "write", "clear"),
                    Str("content", "Text to place on the clipboard for the 'write' action."),
                    Str("format", "Optional clipboard format hint. Only 'text' is supported.", false, "text")),
                Tool("get_environment_variable", "Reads environment variables from process, user, or machine scope.",
                    Str("name", "Environment variable name.", required: true),
                    Str("scope", "Scope to read from.", true, "process", "user", "machine")),
                Tool("set_environment_variable", "Sets or removes environment variables from process, user, or machine scope.",
                    Str("name", "Environment variable name.", required: true),
                    Str("value", "New value; omit or null to delete the variable."),
                    Str("scope", "Scope to write to.", true, "process", "user", "machine")),
                Tool("list_directory", "Lists files and directories with Windows metadata.",
                    Str("path", "Directory path to list.", required: true),
                    Bool("recursive", "Recurse into subdirectories when true."),
                    Bool("include_hidden", "Include hidden and system entries when true."),
                    Str("pattern", "Optional wildcard filename pattern, e.g. *.txt.")),
                Tool("read_file", "Reads text or binary file content with encoding detection.",
                    Str("path", "File path to read.", required: true),
                    Str("encoding", "Text encoding name, e.g. utf-8, utf-16, ascii."),
                    Int("max_bytes", "Maximum number of bytes to read."),
                    Bool("as_base64", "Return raw bytes as base64 instead of decoded text.")),
                Tool("write_file", "Writes file content with explicit overwrite control.",
                    Str("path", "File path to write.", required: true),
                    Str("content", "Content to write to the file.", required: true),
                    Str("encoding", "Text encoding name, e.g. utf-8."),
                    Bool("overwrite", "Overwrite an existing file when true."),
                    Bool("create_directories", "Create missing parent directories when true.")),
                Tool("copy_move_delete_path", "Copies, moves, deletes, or sends files and directories to the recycle bin.",
                    Str("action", "File system operation to perform.", true, "copy", "move", "delete", "recycle"),
                    Str("source_path", "Source file or directory path.", required: true),
                    Str("destination_path", "Destination path for copy and move actions."),
                    Bool("recursive", "Apply the action recursively to directories when true."),
                    Bool("overwrite", "Overwrite an existing destination when true.")),
                Tool("get_file_properties", "Retrieves file metadata, version info, hashes, and security descriptor summary.",
                    Str("path", "File path to inspect.", required: true),
                    Str("hash_algorithm", "Optional content hash to compute.", false, "SHA256", "SHA1", "MD5")),
                Tool("open_path", "Opens a file, folder, URI, or shell verb using the Windows shell.",
                    Str("path_or_uri", "File path, folder path, or URI to open.", required: true),
                    Str("verb", "Optional shell verb, e.g. open, edit, print."),
                    StrArray("arguments", "Optional arguments passed to the target.")),
                Tool("show_in_explorer", "Opens File Explorer with a file or folder selected.",
                    Str("path", "File or folder path to reveal.", required: true)),
                Tool("search_files", "Searches file names and optional file content across one or more roots.",
                    StrArray("roots", "Root directories to search.", required: true),
                    Str("name_pattern", "Optional wildcard filename pattern."),
                    Str("content_query", "Optional text to search for inside files."),
                    Bool("include_hidden", "Include hidden and system files when true."),
                    Int("max_results", "Maximum number of results to return.")),
                Tool("get_display_metrics", "Maps the physical monitor setup."),
                Tool("get_window_screen_info", "Returns screen mapping details for a window handle.",
                    Int("window_handle", "Native window handle (HWND) to resolve.", required: true)),
                Tool("identify_screens", "Returns monitor identity and geometry details for screen identification."),
                Tool("show_notification", "Shows a local Windows notification balloon.",
                    Str("title", "Notification title.", required: true),
                    Str("message", "Notification body text.", required: true),
                    Int("timeout_ms", "How long to display the notification in milliseconds.")),
                Tool("list_services", "Lists Windows services with status and startup metadata.",
                    Str("name_filter", "Case-insensitive substring to match against service names."),
                    Str("status_filter", "Optional service status to filter by, e.g. running, stopped.")),
                Tool("read_registry", "Reads values from a Windows registry key.",
                    Str("hive", "Registry hive.", true, "HKCU", "HKLM", "HKCR", "HKU", "HKCC"),
                    Str("key_path", "Registry key path relative to the hive.", required: true),
                    Str("value_name", "Specific value name to read; omit to read all values.")),
                Tool("list_installed_apps", "Lists installed desktop applications and optional Store package registrations.",
                    Str("name_filter", "Case-insensitive substring to match against application names."),
                    Bool("include_store_apps", "Include Microsoft Store packaged apps when true."),
                    Bool("include_system_components", "Include system components when true.")),
                Tool("launch_app", "Launches an application by path, shell URI, AUMID, or shortcut name.",
                    Str("identifier", "Application identifier matching identifier_type.", required: true),
                    Str("identifier_type", "How to interpret identifier.", true, "path", "shell_uri", "shortcut_name", "aumid"),
                    StrArray("arguments", "Optional command-line arguments.")),
                Tool("show_control_indicator", "Displays computer-control indication state.",
                    Str("message", "Message shown in the control indicator.", required: true),
                    RectParam("bounds", "Optional screen bounds for the indicator overlay.")),
                Tool("hide_control_indicator", "Hides computer-control indication state."),
                Tool("configure_control_indicators", "Configures visual and audio control indicators.",
                    Bool("visual_enabled", "Enable the visual border indicator (default true)."),
                    Bool("audio_enabled", "Enable the audio indicator (default true)."),
                    Str("border_color", "Border color name (default Red)."),
                    Int("border_thickness", "Border thickness in pixels (default 4)."),
                    Int("audio_frequency_hz", "Audio tone frequency in Hz (default 880)."),
                    Int("audio_duration_ms", "Audio tone duration in milliseconds (default 150).")),
                Tool("get_control_indicator_status", "Returns current indicator state."),
                Tool("request_user_confirmation", "Requests local user confirmation.",
                    Str("title", "Confirmation dialog title.", required: true),
                    Str("message", "Confirmation prompt shown to the user.", required: true),
                    Str("risk_level", "Risk level of the pending operation.", true, "low", "medium", "high"),
                    Int("timeout_ms", "How long to wait for a response in milliseconds.")),
                Tool("get_operation_history", "Returns a bounded audit log of recent tool executions.",
                    Int("limit", "Maximum number of audit entries to return (default 50)."),
                    Bool("include_sensitive_arguments", "Include raw tool arguments in the output when true."))
            }
        };
    }

    public async Task<object> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var operationId = Guid.NewGuid().ToString("N");
        var args = ToDictionary(arguments);
        var risk = riskPolicy.Classify(name, args);

        // High-risk tools (process kills, file writes/deletes, env and script
        // execution) are gated behind a local confirmation dialog. The
        // request_user_confirmation tool is the dialog itself, so gating it
        // would be circular; it is always exempt.
        if (requireConfirmation
            && name != "request_user_confirmation"
            && riskPolicy.RequiresConfirmation(name, args))
        {
            var confirmation = controlIndicatorService.RequestUserConfirmation(
                "windows-commander — confirm high-risk action",
                $"Allow the '{name}' tool to run on this machine?",
                risk.ToString(),
                null);

            if (confirmation.Decision != "approved")
            {
                auditLog.Record(new AuditEntry(operationId, name, startedAt, DateTimeOffset.UtcNow, $"blocked:{confirmation.Decision}", args, "High-risk action was not approved by the local user.", risk));
                return ToErrorResult($"Blocked: '{name}' is a high-risk action and the local user did not approve it ({confirmation.Decision}).");
            }
        }

        if (ComputerUseTools.Contains(name))
        {
            notifier.Notify();
            controlIndicatorService.SignalActivity(DescribeActivity(name), risk == RiskLevel.High);
        }

        try
        {
            var result = await DispatchAsync(name, arguments, cancellationToken);
            auditLog.Record(new AuditEntry(operationId, name, startedAt, DateTimeOffset.UtcNow, "success", args, null, risk));

            return ToToolResult(result);
        }
        catch (Exception exception)
        {
            // Per the MCP spec a tool that fails reports the failure inside the
            // result as isError=true rather than as a JSON-RPC protocol error.
            // This keeps the connection healthy and lets the caller see and
            // recover from the error (e.g. a missing or invalid argument).
            auditLog.Record(new AuditEntry(operationId, name, startedAt, DateTimeOffset.UtcNow, "error", args, exception.Message, risk));

            return ToErrorResult($"Tool '{name}' failed: {exception.Message}");
        }
    }

    // Builds the MCP isError result shape shared by tool failures and by
    // confirmation-gated blocks.
    private static object ToErrorResult(string message)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = message }
            },
            isError = true
        };
    }

    // Maps a tool name to a short human-readable phrase for the activity glow
    // label, so the on-screen cue reads "typing" rather than "type_text".
    private static string DescribeActivity(string toolName)
    {
        return toolName switch
        {
            "type_text" => "typing text",
            "send_hotkey" => "pressing a hotkey",
            "keyboard_action" => "pressing keys",
            "mouse_action" => "moving / clicking the mouse",
            "mouse_wheel" => "scrolling",
            "set_cursor_position" => "moving the cursor",
            "input_sequence" => "running an input sequence",
            "focus_window" => "focusing a window",
            "move_resize_window" => "moving / resizing a window",
            "set_window_state" => "changing a window state",
            "capture_screen" or "capture_screen_region" => "capturing the screen",
            "invoke_ui_element" => "invoking a UI element",
            "set_ui_value" => "setting a UI value",
            "launch_app" => "launching an app",
            "open_path" => "opening a path",
            "show_in_explorer" => "opening File Explorer",
            "manage_process" => "managing a process",
            _ => toolName.Replace('_', ' ')
        };
    }

    private static object ToToolResult(object result)
    {
        // Screenshots are returned as MCP image content so clients can view the
        // PNG directly, instead of an unusable multi-megabyte base64 text blob.
        if (result is ScreenCaptureResult capture)
        {
            return new
            {
                content = new object[]
                {
                    new
                    {
                        type = "image",
                        data = capture.PngBase64,
                        mimeType = "image/png"
                    },
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(
                            new { capture.Region, capture.MonitorId, capture.CapturedAt },
                            JsonOptions.Default)
                    }
                }
            };
        }

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

    private async Task<object> DispatchAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        return name switch
        {
            "list_processes" => processService.ListProcesses(GetString(arguments, "filter_name"), GetBool(arguments, "sort_by_memory") ?? false),
            "get_process_details" => processService.GetProcessDetails(GetRequiredInt(arguments, "pid")),
            "manage_process" => processService.ManageProcess(GetRequiredInt(arguments, "pid"), GetRequiredString(arguments, "action")),
            "list_windows" => windowService.ListWindows(),
            "find_window" => windowService.FindWindows(
                GetString(arguments, "title_contains"),
                GetString(arguments, "class_name"),
                GetString(arguments, "process_name"),
                GetInt(arguments, "pid"),
                GetBool(arguments, "visible_only") ?? false,
                GetBool(arguments, "process_name_exact") ?? false),
            "focus_window" => windowService.FocusWindow(GetRequiredLong(arguments, "window_handle")),
            "move_resize_window" => windowService.MoveResizeWindow(
                GetRequiredLong(arguments, "window_handle"),
                GetInt(arguments, "x"),
                GetInt(arguments, "y"),
                GetInt(arguments, "width"),
                GetInt(arguments, "height")),
            "set_window_state" => windowService.SetWindowState(GetRequiredLong(arguments, "window_handle"), GetRequiredString(arguments, "state")),
            "wait_for_window" => await windowService.WaitForWindowAsync(
                GetString(arguments, "title_contains"),
                GetString(arguments, "class_name"),
                GetString(arguments, "process_name"),
                GetInt(arguments, "pid"),
                GetInt(arguments, "timeout_ms") ?? 30000,
                cancellationToken,
                GetBool(arguments, "process_name_exact") ?? false),
            "enumerate_child_windows" => windowService.EnumerateChildWindows(GetRequiredLong(arguments, "window_handle")),
            "mouse_action" => inputService.MouseAction(
                GetRequiredString(arguments, "action"),
                GetString(arguments, "button"),
                GetInt(arguments, "x"),
                GetInt(arguments, "y"),
                GetLong(arguments, "target_hwnd")),
            "type_text" => await inputService.TypeTextAsync(GetRequiredString(arguments, "text"), GetInt(arguments, "speed_ms"), cancellationToken),
            "send_hotkey" => inputService.SendHotkey(GetRequiredStringArray(arguments, "modifiers"), GetRequiredString(arguments, "key")),
            "keyboard_action" => await inputService.KeyboardActionAsync(
                GetRequiredString(arguments, "action"),
                GetRequiredString(arguments, "key"),
                GetInt(arguments, "repeat"),
                GetInt(arguments, "delay_ms"),
                cancellationToken),
            "mouse_wheel" => inputService.MouseWheel(
                GetRequiredString(arguments, "direction"),
                GetRequiredInt(arguments, "amount"),
                GetLong(arguments, "target_hwnd"),
                GetInt(arguments, "x"),
                GetInt(arguments, "y")),
            "get_cursor_position" => inputService.GetCursorPosition(),
            "set_cursor_position" => await inputService.SetCursorPositionAsync(GetRequiredInt(arguments, "x"), GetRequiredInt(arguments, "y"), GetInt(arguments, "duration_ms"), cancellationToken),
            "input_sequence" => await inputService.InputSequenceAsync(GetInputSequenceSteps(arguments), GetBool(arguments, "abort_on_error") ?? true, cancellationToken),
            "capture_screen" => visionService.CaptureScreen(
                GetRequiredString(arguments, "target"),
                GetLong(arguments, "hwnd"),
                GetInt(arguments, "max_dimension")),
            "capture_screen_region" => visionService.CaptureScreenRegion(
                GetRequiredInt(arguments, "x"),
                GetRequiredInt(arguments, "y"),
                GetRequiredInt(arguments, "width"),
                GetRequiredInt(arguments, "height"),
                GetString(arguments, "monitor_id"),
                GetInt(arguments, "max_dimension")),
            "read_ui_tree" => uiAutomationService.ReadUiTree(
                GetRequiredLong(arguments, "hwnd"),
                GetInt(arguments, "max_depth") ?? 5),
            "find_ui_element" => uiAutomationService.FindUiElement(
                GetRequiredLong(arguments, "hwnd"),
                GetString(arguments, "name_contains"),
                GetString(arguments, "automation_id"),
                GetString(arguments, "control_type"),
                GetString(arguments, "class_name"),
                GetBool(arguments, "enabled_only") ?? false,
                GetInt(arguments, "max_depth") ?? 5),
            "invoke_ui_element" => uiAutomationService.InvokeUiElement(GetRequiredString(arguments, "element_ref"), GetRequiredString(arguments, "action")),
            "set_ui_value" => uiAutomationService.SetUiValue(GetRequiredString(arguments, "element_ref"), GetRequiredString(arguments, "value")),
            "get_ui_element_details" => uiAutomationService.GetUiElementDetails(GetRequiredString(arguments, "element_ref")),
            "ocr_screen" => visionService.OcrScreen(GetRequiredString(arguments, "target"), GetLong(arguments, "hwnd"), GetRect(arguments, "region")),
            "detect_visual_elements" => visionService.DetectVisualElements(
                GetRequiredString(arguments, "target"),
                GetLong(arguments, "hwnd"),
                GetRect(arguments, "region"),
                GetStringArray(arguments, "element_types")),
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
            "get_display_metrics" => screenService.GetDisplayMetrics(),
            "get_window_screen_info" => screenService.GetWindowScreenInfo(GetRequiredLong(arguments, "window_handle")),
            "identify_screens" => screenService.GetScreenDetails(),
            "show_notification" => screenService.ShowNotification(
                GetRequiredString(arguments, "title"),
                GetRequiredString(arguments, "message"),
                GetInt(arguments, "timeout_ms")),
            "list_services" => windowsServiceDiscoveryService.ListServices(
                GetString(arguments, "name_filter"),
                GetString(arguments, "status_filter")),
            "read_registry" => registryService.ReadRegistry(
                GetRequiredString(arguments, "hive"),
                GetRequiredString(arguments, "key_path"),
                GetString(arguments, "value_name")),
            "list_installed_apps" => applicationService.ListInstalledApps(
                GetString(arguments, "name_filter"),
                GetBool(arguments, "include_store_apps") ?? false,
                GetBool(arguments, "include_system_components") ?? false),
            "launch_app" => applicationService.LaunchApp(
                GetRequiredString(arguments, "identifier"),
                GetRequiredString(arguments, "identifier_type"),
                GetStringArray(arguments, "arguments")),
            "show_control_indicator" => controlIndicatorService.ShowControlIndicator(
                GetRequiredString(arguments, "message"),
                GetRect(arguments, "bounds")),
            "hide_control_indicator" => controlIndicatorService.HideControlIndicator(),
            "configure_control_indicators" => controlIndicatorService.ConfigureControlIndicators(GetControlIndicatorConfig(arguments)),
            "get_control_indicator_status" => controlIndicatorService.GetControlIndicatorStatus(),
            "request_user_confirmation" => controlIndicatorService.RequestUserConfirmation(
                GetRequiredString(arguments, "title"),
                GetRequiredString(arguments, "message"),
                GetRequiredString(arguments, "risk_level"),
                GetInt(arguments, "timeout_ms")),
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

    // A single declared parameter: its property name, its JSON Schema fragment,
    // and whether the tool requires it.
    private sealed record ToolParam(string Name, object Schema, bool Required);

    private static McpTool Tool(string name, string description, params ToolParam[] parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = parameter.Schema;
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return new McpTool(name, description, schema);
    }

    private static ToolParam Str(string name, string description, bool required = false, params string[] allowed)
    {
        var schema = new Dictionary<string, object> { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0)
        {
            schema["enum"] = allowed;
        }

        return new ToolParam(name, schema, required);
    }

    private static ToolParam Int(string name, string description, bool required = false)
    {
        return new ToolParam(name, IntegerSchema(description), required);
    }

    private static ToolParam Bool(string name, string description)
    {
        return new ToolParam(name, new Dictionary<string, object> { ["type"] = "boolean", ["description"] = description }, false);
    }

    private static ToolParam StrArray(string name, string description, bool required = false)
    {
        return new ToolParam(
            name,
            new Dictionary<string, object>
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            required);
    }

    private static ToolParam RectParam(string name, string description, bool required = false)
    {
        return new ToolParam(
            name,
            new Dictionary<string, object>
            {
                ["type"] = "object",
                ["description"] = description,
                ["properties"] = new Dictionary<string, object>
                {
                    ["x"] = IntegerSchema("Left position in pixels."),
                    ["y"] = IntegerSchema("Top position in pixels."),
                    ["width"] = IntegerSchema("Width in pixels."),
                    ["height"] = IntegerSchema("Height in pixels.")
                },
                ["required"] = new[] { "x", "y", "width", "height" }
            },
            required);
    }

    private static ToolParam MapParam(string name, string description)
    {
        return new ToolParam(
            name,
            new Dictionary<string, object>
            {
                ["type"] = "object",
                ["description"] = description,
                ["additionalProperties"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            false);
    }

    private static ToolParam StepsParam(string name, string description, bool required = false)
    {
        var stepSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["type"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Step kind.",
                    ["enum"] = new[] { "mouse", "text", "keyboard", "hotkey", "delay" }
                },
                ["action"] = StringSchema("Action for mouse or keyboard steps."),
                ["text"] = StringSchema("Text for text steps."),
                ["key"] = StringSchema("Key for keyboard or hotkey steps."),
                ["modifiers"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["description"] = "Modifier keys for hotkey steps.",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" }
                },
                ["x"] = IntegerSchema("X coordinate for mouse steps."),
                ["y"] = IntegerSchema("Y coordinate for mouse steps."),
                ["delay_ms"] = IntegerSchema("Delay in milliseconds for delay steps.")
            },
            ["required"] = new[] { "type" }
        };

        return new ToolParam(
            name,
            new Dictionary<string, object>
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = stepSchema
            },
            required);
    }

    private static Dictionary<string, object> StringSchema(string description)
    {
        return new Dictionary<string, object> { ["type"] = "string", ["description"] = description };
    }

    private static Dictionary<string, object> IntegerSchema(string description)
    {
        return new Dictionary<string, object> { ["type"] = "integer", ["description"] = description };
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

    private static long? GetLong(JsonElement? arguments, string name)
    {
        return TryGetProperty(arguments, name, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static long GetRequiredLong(JsonElement? arguments, string name)
    {
        return GetLong(arguments, name) ?? throw new ArgumentException($"Missing or invalid integer argument: {name}");
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

    private static RectBounds? GetRect(JsonElement? arguments, string name)
    {
        if (!TryGetProperty(arguments, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Argument must be a rectangle object: {name}");
        }

        return new RectBounds(
            GetRequiredInt(value, "x"),
            GetRequiredInt(value, "y"),
            GetRequiredInt(value, "width"),
            GetRequiredInt(value, "height"));
    }

    private static ControlIndicatorConfig GetControlIndicatorConfig(JsonElement? arguments)
    {
        return new ControlIndicatorConfig(
            GetBool(arguments, "visual_enabled") ?? true,
            GetBool(arguments, "audio_enabled") ?? true,
            GetString(arguments, "border_color") ?? "Red",
            GetInt(arguments, "border_thickness") ?? 4,
            GetInt(arguments, "audio_frequency_hz") ?? 880,
            GetInt(arguments, "audio_duration_ms") ?? 150);
    }

    private static IReadOnlyList<InputSequenceStep> GetInputSequenceSteps(JsonElement? arguments)
    {
        if (!TryGetProperty(arguments, "steps", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Missing or invalid input sequence steps.");
        }

        return value.EnumerateArray()
            .Select(step => new InputSequenceStep(
                GetRequiredString(step, "type"),
                GetString(step, "action"),
                GetString(step, "text"),
                GetString(step, "key"),
                GetStringArray(step, "modifiers"),
                GetInt(step, "x"),
                GetInt(step, "y"),
                GetInt(step, "delay_ms")))
            .ToArray();
    }

    private static int? GetInt(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static int GetRequiredInt(JsonElement arguments, string name)
    {
        return GetInt(arguments, name) ?? throw new ArgumentException($"Missing or invalid integer argument: {name}");
    }

    private static string? GetString(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string GetRequiredString(JsonElement arguments, string name)
    {
        return GetString(arguments, name) ?? throw new ArgumentException($"Missing or invalid string argument: {name}");
    }

    private static IReadOnlyList<string>? GetStringArray(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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

    private static bool TryGetProperty(JsonElement? arguments, string name, out JsonElement value)
    {
        value = default;
        return arguments is not null
            && arguments.Value.ValueKind == JsonValueKind.Object
            && arguments.Value.TryGetProperty(name, out value);
    }
}
