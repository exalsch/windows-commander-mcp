# Tool Coverage

This document tracks implementation status against `specs/initial_definition.md`.

## Implemented

### Process & Window Orchestration

- `list_processes`
- `get_process_details`
- `manage_process`
- `list_windows`
- `find_window`
- `focus_window`
- `move_resize_window`
- `set_window_state`
- `wait_for_window`
- `enumerate_child_windows`

### Hardware Simulation

- `mouse_action`
- `type_text`
- `send_hotkey`
- `keyboard_action`
- `mouse_wheel`
- `get_cursor_position`
- `set_cursor_position`
- `input_sequence`

### Vision & Context Extraction

- `capture_screen`
- `capture_screen_region`
- `read_ui_tree`
- `find_ui_element`
- `invoke_ui_element`
- `set_ui_value`
- `get_ui_element_details`
- `ocr_screen` via local UI/window visible text extraction
- `detect_visual_elements` via local window/UI metadata candidates

### System State & Execution

- `execute_powershell`
- `execute_process`
- `clipboard_access` for text only
- `get_screen_details`
- `get_screen_at_point`
- `get_display_metrics`
- `get_window_screen_info`
- `identify_screens`
- `show_notification`
- `get_system_info`
- `get_environment_variable`
- `set_environment_variable`

### File System & Shell Integration

- `list_directory`
- `read_file`
- `write_file`
- `copy_move_delete_path` for `copy`, `move`, and permanent `delete`
- `get_file_properties` with file metadata, version info, and optional `SHA256`, `SHA1`, or `MD5` hashes
- `open_path`
- `show_in_explorer`
- `search_files`

### Windows Services, Registry & Scheduled Tasks

- `list_services`
- `read_registry`

### Applications, Packages & Shortcuts

- `list_installed_apps`
- `launch_app`

### Safety, Permissions & Session Control

- `get_operation_history`
- `show_control_indicator`
- `hide_control_indicator`
- `configure_control_indicators`
- `get_control_indicator_status`
- `request_user_confirmation`
- Risk classification service
- In-memory audit log
- Sensitive argument redaction

## Partially Implemented

- `clipboard_access`
  - Implemented: `text`
  - Pending: `html`, `file_drop_list`, `image`

- `copy_move_delete_path`
  - Implemented: `copy`, `move`, `delete`
  - Pending: `recycle`

- `get_file_properties`
  - Implemented: metadata, version info, hashes
  - Pending: alternate data streams and security descriptor summary

## Implementation Notes

- Current MCP tool schemas are minimal object schemas. Rich per-tool JSON schemas should be added as the dispatcher matures.
- Control indicators use a persistent topmost WPF border overlay window plus the configured audio cue.
- `ocr_screen` is local-only and uses visible window/UI text metadata; integrating a native OCR engine remains a future enhancement.
- Current implementation targets Windows interactive desktop use and `.NET 10`.
