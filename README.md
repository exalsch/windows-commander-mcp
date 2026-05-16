# Windows Commander MCP

Windows Commander MCP is a Windows-aware `.NET 8` stdio MCP server that exposes process, window, screen, execution, clipboard, environment, file system, and shell automation tools for MCP clients.

## Status

This repository is under active implementation from `specs/initial_definition.md`.

Implemented slices currently include:

- MCP JSON-RPC stdio host
- Process discovery
- Window discovery/search
- Screen and system metadata
- PowerShell/native process execution
- Text clipboard access
- Environment variable access
- File system and shell integration
- Process/window management
- Hardware input simulation
- Screen capture
- UI Automation tree/action tools
- Local OCR-style text extraction from UI/window metadata
- Visual candidate detection from local metadata
- Display metrics, screen identification, and window/screen mapping
- Windows notification balloons
- Windows service discovery
- Registry reads
- Installed application discovery and launching
- In-memory audit logging
- Risk classification foundation
- Unit tests for safety and file system behavior

See `docs/tool-coverage.md` for detailed coverage.

## Requirements

- Windows interactive desktop session
- .NET 8 SDK
- PowerShell 7+ available as `pwsh.exe` for `execute_powershell`

Some tools require desktop-session access. Elevated target applications may not be controllable from a non-elevated server process.

## Repository Layout

```text
src/
  WindowsCommander.McpServer/   MCP stdio entry point and tool dispatcher
  WindowsCommander.Core/        Shared models, interfaces, operation and safety contracts
  WindowsCommander.Windows/     Windows-specific service implementations
  WindowsCommander.Safety/      Audit log and risk policy foundation
  WindowsCommander.Vision/      Placeholder project for future capture/OCR/vision features
tests/
  WindowsCommander.Tests/       Unit and Windows-local tests
specs/
  initial_definition.md         Full target specification
```

## Build

```powershell
dotnet build WindowsCommander.slnx
```

## Test

```powershell
dotnet test WindowsCommander.slnx
```

Current test coverage includes:

- Risk classification
- Audit redaction
- File write/read/properties round trip
- Directory listing behavior

## Run Locally

```powershell
dotnet run --project src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj
```

The server communicates over line-delimited JSON-RPC via stdio.

Example initialize request:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
```

Example tool list request:

```json
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
```

Example tool call:

```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_system_info","arguments":{}}}
```

## MCP Client Configuration

Use the published executable as a stdio MCP server command in your MCP client.

Example command shape:

```json
{
  "mcpServers": {
    "windows-commander": {
      "command": "C:\\PROGRAMMING\\MCP_Servers\\windows-commander-mcp\\src\\WindowsCommander.McpServer\\bin\\Release\\net8.0-windows\\win-x64\\publish\\WindowsCommander.McpServer.exe",
      "args": []
    }
  }
}
```

Publish first and point the MCP client at the generated executable.

## Publish

```powershell
dotnet publish src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release -r win-x64 --self-contained true
```

The generated executable is:

```text
src\WindowsCommander.McpServer\bin\Release\net8.0-windows\win-x64\publish\WindowsCommander.McpServer.exe
```

## Safety Model

The project includes a safety foundation but not the full visual/acoustic control layer yet.

Current behavior:

- Tool executions are recorded in an in-memory audit log.
- Sensitive audit argument names such as `password`, `token`, `secret`, and `key` are redacted by default.
- Risk classification marks destructive or command-execution tools as high risk.

Planned behavior:

- Configurable confirmation policy enforcement
- WPF visual control indicators
- Audio control cues
- User confirmation dialogs
- Persistent or exportable audit history

See `docs/safety.md`.

## Important Limitations

‼️ Some deep fidelity items remain partial: native OCR engine integration, recycle-bin operations, non-text clipboard formats, scheduled task management, alternate data streams, and security descriptor summaries.

‼️ `clipboard_access` currently supports text format only.

‼️ `copy_move_delete_path` currently supports `copy`, `move`, and permanent `delete`; `recycle` is not implemented yet.

‼️ `get_file_properties` does not yet include alternate data stream names or security descriptor summary.

## Manual Validation

See `docs/testing.md` for manual test cases with IDs such as `TC1`, `TC2`, and `TC3`.

## Design Goals

- Keep MCP tool handlers thin.
- Keep Windows-specific APIs behind services.
- Make destructive and computer-control operations observable and auditable.
- Use Windows virtual-screen coordinates consistently for future UI/input/vision work.
- Prefer local Windows/.NET-compatible dependencies; no cloud OCR or vision services.
