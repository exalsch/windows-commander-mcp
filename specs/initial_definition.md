# Windows-Aware MCP Server (.NET) - Tool Definitions

This document outlines the core capabilities exposed by the custom .NET `stdio` MCP Server, designed for high-performance Windows OS control with built-in visual and acoustic safety features.

## 1. Process & Window Orchestration
*Deep integration with Windows processes and their associated UI threads.*

*   **`list_processes`**
    *   **Description:** Retrieves a list of currently running processes.
    *   **Parameters:** `filter_name` (optional string), `sort_by_memory` (optional boolean).
    *   **Returns:** Array of objects containing `PID`, `ProcessName`, `MemoryUsageMB`, and `MainWindowTitle`.
*   **`get_process_details`**
    *   **Description:** Fetches deep system information for a specific process.
    *   **Parameters:** `pid` (integer).
    *   **Returns:** Details that allow efficient window management, including `ExecutablePath`, `Status` (Responding/Hung), and a list of all `HWNDs` (window handles) owned by the process.
*   **`manage_process`**
    *   **Description:** Executes process-level lifecycle commands.
    *   **Parameters:** `action` ("start" | "kill" | "restart"), `target` (PID or executable path).
*   **`list_windows`**
    *   **Description:** Returns a hierarchical list of active, visible windows.
    *   **Returns:** Array containing `HWND`, `Title`, `OwningPID`, `IsMinimized`, and `BoundingRect` (X, Y, Width, Height).
*   **`focus_window`**
    *   **Description:** Brings a specific window to the foreground and restores it if minimized.
    *   **Parameters:** `hwnd` (integer).
*   **`move_resize_window`**
    *   **Description:** Moves and resizes a window using virtual-screen coordinates.
    *   **Parameters:** `hwnd` (integer), `x` (integer), `y` (integer), `width` (integer), `height` (integer).
    *   **Safety Trigger:** Renders a preview frame before applying the final placement.
*   **`set_window_state`**
    *   **Description:** Changes a window state without terminating the owning process.
    *   **Parameters:** `hwnd` (integer), `state` ("minimize" | "maximize" | "restore" | "show" | "hide" | "close").
*   **`find_window`**
    *   **Description:** Locates windows by title, class name, executable name, process ID, or visibility state.
    *   **Parameters:** `title_contains` (optional string), `class_name` (optional string), `process_name` (optional string), `pid` (optional integer), `visible_only` (optional boolean).
    *   **Returns:** Array containing `HWND`, `Title`, `ClassName`, `OwningPID`, `ProcessName`, `IsVisible`, `IsMinimized`, and `BoundingRect`.
*   **`wait_for_window`**
    *   **Description:** Waits for a matching window to appear, disappear, become focused, or become responsive.
    *   **Parameters:** `match` (object using `find_window` filters), `condition` ("exists" | "not_exists" | "focused" | "responsive"), `timeout_ms` (integer).
    *   **Returns:** Matching window details or a timeout error.
*   **`enumerate_child_windows`**
    *   **Description:** Lists child window handles for legacy Win32 applications where UI Automation coverage is incomplete.
    *   **Parameters:** `hwnd` (integer), `recursive` (optional boolean).
    *   **Returns:** Array containing child `HWND`, `ClassName`, `Text`, `ControlId`, and `BoundingRect`.

## 2. Hardware Simulation
*Native Win32 `SendInput` execution. All actions trigger the WPF visual feedback overlay.*

*   **`mouse_action`**
    *   **Description:** Performs mouse movements and clicks.
    *   **Parameters:** `action` ("move" | "click" | "double_click" | "drag"), `button` ("left" | "right" | "middle"), `x` (integer), `y` (integer), `target_hwnd` (optional integer - if provided, calculates the center of the window automatically).
    *   **Safety Trigger:** Renders a visible frame around the target application window or screen area and plays the computer-control audio indicator while input is being performed.
*   **`type_text`**
    *   **Description:** Injects a stream of keystrokes naturally.
    *   **Parameters:** `text` (string), `speed_ms` (optional integer - delay between keystrokes).
    *   **Safety Trigger:** Renders a visible frame around the focused application window receiving input and plays the computer-control audio indicator while typing.
*   **`send_hotkey`**
    *   **Description:** Executes system-level keyboard shortcuts.
    *   **Parameters:** `modifiers` (array of strings, e.g., ["ctrl", "shift"]), `key` (string).
    *   **Safety Trigger:** Renders a visible frame around the focused application window receiving input and plays the computer-control audio indicator while the hotkey is sent.
*   **`keyboard_action`**
    *   **Description:** Sends low-level key press, release, or tap events for non-text keys.
    *   **Parameters:** `action` ("press" | "release" | "tap"), `key` (string), `repeat` (optional integer), `delay_ms` (optional integer).
    *   **Safety Trigger:** Renders a visible frame around the focused application window receiving input and plays the computer-control audio indicator while keyboard control is active.
*   **`mouse_wheel`**
    *   **Description:** Scrolls vertically or horizontally at the current cursor location or over a target window.
    *   **Parameters:** `direction` ("up" | "down" | "left" | "right"), `amount` (integer), `target_hwnd` (optional integer), `x` (optional integer), `y` (optional integer).
    *   **Safety Trigger:** Renders a visible frame around the target application window or screen area and plays the computer-control audio indicator while scrolling.
*   **`get_cursor_position`**
    *   **Description:** Returns the current mouse cursor position in virtual-screen coordinates.
    *   **Returns:** `x`, `y`, `monitor_id`, and `is_over_window` metadata.
*   **`set_cursor_position`**
    *   **Description:** Moves the cursor to an absolute virtual-screen coordinate without clicking.
    *   **Parameters:** `x` (integer), `y` (integer), `duration_ms` (optional integer).
    *   **Safety Trigger:** Renders a visible frame around the destination screen and plays the computer-control audio indicator while cursor control is active.
*   **`input_sequence`**
    *   **Description:** Executes an ordered, validated sequence of mouse, keyboard, text, delay, and hotkey actions atomically.
    *   **Parameters:** `steps` (array of input action objects), `abort_on_error` (optional boolean).
    *   **Safety Trigger:** Renders a visible frame around each active target window or screen, shows the number of pending actions, and plays the computer-control audio indicator for the full sequence duration.

## 3. Vision & Context Extraction
*Bypassing abstraction layers by using .NET native UI Automation and Graphics libraries.*

*   **`capture_screen`**
    *   **Description:** Captures pixels from the screen for visual reasoning.
    *   **Parameters:** `target` ("full_screen" | "active_window" | specific `hwnd`).
    *   **Returns:** Base64 encoded PNG string.
    *   **Safety Trigger:** Renders a visible frame on the outside border of each captured screen, or around the captured application window, and plays the computer-control audio indicator while the capture is taken.
*   **`capture_screen_region`**
    *   **Description:** Captures pixels from a specific rectangular region in virtual-screen coordinates.
    *   **Parameters:** `x` (integer), `y` (integer), `width` (integer), `height` (integer), `monitor_id` (optional string).
    *   **Returns:** Base64 encoded PNG string plus the captured region, monitor ID, DPI scale, and timestamp.
    *   **Safety Trigger:** Renders a visible frame around the captured region and plays the computer-control audio indicator while the capture is taken.
*   **`read_ui_tree`**
    *   **Description:** Dumps the Windows Accessibility/UIAutomation tree into structured text, allowing the LLM to "read" the app without taking a screenshot.
    *   **Parameters:** `hwnd` (integer).
    *   **Returns:** JSON representation of UI elements (Buttons, TextBoxes, CheckBoxes) including their control types, names, values, and localized bounding boxes.
*   **`find_ui_element`**
    *   **Description:** Searches the UI Automation tree for interactable elements.
    *   **Parameters:** `hwnd` (integer), `name_contains` (optional string), `automation_id` (optional string), `control_type` (optional string), `class_name` (optional string), `enabled_only` (optional boolean).
    *   **Returns:** Array of matching elements with stable element references, names, roles, values, states, and bounding boxes.
*   **`invoke_ui_element`**
    *   **Description:** Invokes the default action of a UI Automation element, such as clicking a button or selecting a menu item.
    *   **Parameters:** `element_ref` (string), `action` ("invoke" | "select" | "expand" | "collapse" | "toggle" | "focus").
    *   **Safety Trigger:** Highlights the target element before invocation.
*   **`set_ui_value`**
    *   **Description:** Sets the value of a UI Automation element that supports editable text or range patterns.
    *   **Parameters:** `element_ref` (string), `value` (string).
    *   **Safety Trigger:** Highlights the target element before writing.
*   **`get_ui_element_details`**
    *   **Description:** Retrieves detailed UI Automation patterns, supported actions, current value, selection state, and hierarchy context for a single element.
    *   **Parameters:** `element_ref` (string).
*   **`ocr_screen`**
    *   **Description:** Performs OCR over a screenshot region to extract visible text when UI Automation is unavailable.
    *   **Parameters:** `target` ("full_screen" | "active_window" | specific `hwnd`), `region` (optional object with X, Y, Width, Height).
    *   **Returns:** Text blocks with confidence scores and bounding boxes.
    *   **Safety Trigger:** Renders a visible frame around the OCR region and plays the computer-control audio indicator while OCR is performed.
*   **`detect_visual_elements`**
    *   **Description:** Detects common visual targets such as buttons, text fields, icons, menus, dialogs, and progress indicators from captured pixels.
    *   **Parameters:** `target` ("full_screen" | "active_window" | specific `hwnd`), `region` (optional object), `element_types` (optional array of strings).
    *   **Returns:** Candidate elements with labels, confidence scores, and bounding boxes.
    *   **Safety Trigger:** Renders a visible frame around the analyzed screen, window, or region and plays the computer-control audio indicator while analysis is performed.

## 4. System State & Execution
*General environment controls.*

*   **`execute_powershell`**
    *   **Description:** Runs a PowerShell script or command silently.
    *   **Parameters:** `command` (string), `working_directory` (optional string), `timeout_ms` (optional integer), `environment` (optional object).
    *   **Returns:** Standard Output, Standard Error, exit code, elapsed time, and timeout status.
    *   **Safety Trigger:** Acoustic feedback (pulsing sound) plays while the command executes.
*   **`execute_process`**
    *   **Description:** Starts a native executable with explicit arguments and controlled output capture.
    *   **Parameters:** `executable_path` (string), `arguments` (optional array of strings), `working_directory` (optional string), `timeout_ms` (optional integer), `wait_for_exit` (optional boolean).
    *   **Returns:** Process ID and, when `wait_for_exit` is true, Standard Output, Standard Error, exit code, and elapsed time.
*   **`clipboard_access`**
    *   **Description:** Interacts with the system clipboard.
    *   **Parameters:** `action` ("read" | "write" | "clear"), `content` (optional string), `format` (optional "text" | "html" | "file_drop_list" | "image").
*   **`get_display_metrics`**
    *   **Description:** Maps the physical monitor setup.
    *   **Returns:** Array of connected monitors detailing `Resolution`, `VirtualCoordinates` (for multi-monitor mapping), `RefreshRate`, and `DpiScalingFactor`.
*   **`get_screen_details`**
    *   **Description:** Retrieves detailed information about every attached screen and its relationship to the Windows virtual desktop.
    *   **Returns:** Array containing `MonitorId`, `DeviceName`, `FriendlyName`, `IsPrimary`, `Bounds`, `WorkingArea`, `DpiScale`, `Orientation`, `Resolution`, `ColorDepth`, `RefreshRate`, `AdapterName`, and `IsActive`.
*   **`get_screen_at_point`**
    *   **Description:** Resolves which screen contains a specific virtual-screen coordinate.
    *   **Parameters:** `x` (integer), `y` (integer).
    *   **Returns:** Matching screen details, local coordinates within that screen, and whether the point is inside the working area.
*   **`get_window_screen_info`**
    *   **Description:** Determines which screen or screens contain a window and how much of the window is visible on each screen.
    *   **Parameters:** `hwnd` (integer).
    *   **Returns:** Window bounds, owning monitor IDs, primary containing monitor, visible area percentages, and off-screen status.
*   **`identify_screens`**
    *   **Description:** Temporarily displays a large visual label on each attached screen so the user can identify monitor IDs and layout.
    *   **Parameters:** `duration_ms` (optional integer).
    *   **Safety Trigger:** Renders labels and outside-border frames on all screens and plays the computer-control audio indicator while identification is visible.
*   **`get_system_info`**
    *   **Description:** Retrieves host-level metadata useful for automation decisions.
    *   **Returns:** OS version, machine name, current user, integrity level, architecture, uptime, battery status, locale, timezone, and active session status.
*   **`get_environment_variable`**
    *   **Description:** Reads environment variables from process, user, or machine scope.
    *   **Parameters:** `name` (string), `scope` ("process" | "user" | "machine").
*   **`set_environment_variable`**
    *   **Description:** Sets or removes environment variables from process, user, or machine scope.
    *   **Parameters:** `name` (string), `value` (optional string), `scope` ("process" | "user" | "machine").
*   **`show_notification`**
    *   **Description:** Displays a Windows toast or overlay notification for user-visible status reporting.
    *   **Parameters:** `title` (string), `message` (string), `severity` ("info" | "success" | "warning" | "error"), `duration_ms` (optional integer).

## 5. File System & Shell Integration
*Safe, Windows-aware file and shell operations with path normalization.*

*   **`list_directory`**
    *   **Description:** Lists files and directories with Windows metadata.
    *   **Parameters:** `path` (string), `recursive` (optional boolean), `include_hidden` (optional boolean), `pattern` (optional string).
    *   **Returns:** Array containing path, type, size, timestamps, attributes, and extension.
*   **`read_file`**
    *   **Description:** Reads text or binary file content with encoding detection.
    *   **Parameters:** `path` (string), `encoding` (optional string), `max_bytes` (optional integer), `as_base64` (optional boolean).
*   **`write_file`**
    *   **Description:** Writes file content with explicit overwrite control.
    *   **Parameters:** `path` (string), `content` (string), `encoding` (optional string), `overwrite` (optional boolean), `create_directories` (optional boolean).
*   **`copy_move_delete_path`**
    *   **Description:** Copies, moves, deletes, or sends files and directories to the recycle bin.
    *   **Parameters:** `action` ("copy" | "move" | "delete" | "recycle"), `source_path` (string), `destination_path` (optional string), `recursive` (optional boolean), `overwrite` (optional boolean).
*   **`get_file_properties`**
    *   **Description:** Retrieves file metadata, version info, hashes, alternate data stream names, and security descriptor summary.
    *   **Parameters:** `path` (string), `hash_algorithm` (optional "SHA256" | "SHA1" | "MD5").
*   **`open_path`**
    *   **Description:** Opens a file, folder, URI, or shell verb using the Windows shell.
    *   **Parameters:** `path_or_uri` (string), `verb` (optional string), `arguments` (optional array of strings).
    *   **Safety Trigger:** Visual confirmation for shell verbs that launch UI applications.
*   **`show_in_explorer`**
    *   **Description:** Opens File Explorer with a file or folder selected.
    *   **Parameters:** `path` (string).
*   **`search_files`**
    *   **Description:** Searches file names and optional file content across one or more roots.
    *   **Parameters:** `roots` (array of strings), `name_pattern` (optional string), `content_query` (optional string), `include_hidden` (optional boolean), `max_results` (optional integer).

## 6. Windows Services, Registry & Scheduled Tasks
*Administrative Windows management tools with explicit safety boundaries.*

*   **`list_services`**
    *   **Description:** Lists Windows services with status and startup metadata.
    *   **Parameters:** `name_filter` (optional string), `status` (optional "running" | "stopped" | "paused").
*   **`read_registry`**
    *   **Description:** Reads a registry key or value from supported hives.
    *   **Parameters:** `hive` ("HKCU" | "HKLM" | "HKCR" | "HKU" | "HKCC"), `path` (string), `value_name` (optional string).

## 7. Applications, Packages & Shortcuts
*Discovery and launch operations for installed software and Windows app models.*

*   **`list_installed_apps`**
    *   **Description:** Lists installed desktop applications and Microsoft Store packages.
    *   **Parameters:** `name_filter` (optional string), `include_store_apps` (optional boolean), `include_system_components` (optional boolean).
    *   **Returns:** Name, publisher, version, install location, uninstall command, package family name, and app user model ID when available.
*   **`launch_app`**
    *   **Description:** Launches an application by executable path, app user model ID, shell URI, or Start Menu shortcut.
    *   **Parameters:** `identifier` (string), `identifier_type` ("path" | "aumid" | "shell_uri" | "shortcut_name"), `arguments` (optional array of strings).
    *   **Safety Trigger:** Visual launch notification with resolved target.

## 8. Safety, Permissions & Session Control
*Cross-cutting guardrails that make automation observable, interruptible, and auditable.*

*   **`show_control_indicator`**
    *   **Description:** Displays an explicit computer-control indication before or during automation, using an outside-border frame and audio cue.
    *   **Parameters:** `target` ("screen" | "all_screens" | "window" | "region"), `hwnd` (optional integer), `monitor_id` (optional string), `region` (optional object with X, Y, Width, Height), `message` (optional string), `duration_ms` (integer), `audio_enabled` (optional boolean).
    *   **Returns:** Indicator ID, resolved target bounds, affected monitor IDs, and expiration timestamp.
*   **`hide_control_indicator`**
    *   **Description:** Hides a previously displayed computer-control indication.
    *   **Parameters:** `indicator_id` (string).
*   **`configure_control_indicators`**
    *   **Description:** Configures the default visual frame and audio behavior used by input, screenshot, OCR, and UI automation tools.
    *   **Parameters:** `frame_enabled` (optional boolean), `audio_enabled` (optional boolean), `frame_thickness_px` (optional integer), `frame_color` (optional string), `pulse_enabled` (optional boolean), `audio_profile` (optional string), `minimum_visible_duration_ms` (optional integer).
    *   **Returns:** Effective control-indicator configuration.
*   **`get_control_indicator_status`**
    *   **Description:** Reports currently active visual and audio indicators.
    *   **Returns:** Active indicator IDs, target bounds, affected monitor IDs, remaining duration, and audio state.
*   **`request_user_confirmation`**
    *   **Description:** Displays a modal confirmation prompt before high-risk actions.
    *   **Parameters:** `title` (string), `message` (string), `risk_level` ("low" | "medium" | "high"), `timeout_ms` (optional integer).
    *   **Returns:** User decision and timestamp.
*   **`get_operation_history`**
    *   **Description:** Returns a bounded audit log of recent tool executions.
    *   **Parameters:** `limit` (optional integer), `include_sensitive_arguments` (optional boolean).
    *   **Returns:** Operation IDs, tool names, start and end times, status, redacted arguments, and error summaries.