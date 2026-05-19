# Windows Commander MCP

Windows Commander MCP is a Windows-aware `.NET 10` stdio MCP server that exposes process, window, screen, execution, clipboard, environment, file system, and shell automation tools for MCP clients.

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
- .NET 10 SDK
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

## Install (release package)

Each tagged release publishes a ready-to-run, self-contained package on the
[GitHub Releases page](../../releases) — no .NET runtime or build tools needed
on the target machine.

1. Download `windows-commander-mcp-v<version>-win-x64.zip` from the latest
   release.
2. (Optional) Verify it against the published `.sha256` file:
   `(Get-FileHash <zip> -Algorithm SHA256).Hash`.
3. Unzip it to a stable folder, e.g. `C:\Tools\windows-commander-mcp`.
4. Point your MCP client's `.mcp.json` at the `WindowsCommander.McpServer.exe`
   at the root of the unzipped folder (see *MCP Client Configuration* below).

Releases are produced automatically: pushing a `v*` git tag runs the
`.github/workflows/release.yml` workflow, which tests, packages, and publishes
the zip.

### Building a package locally

`tools/package-release.ps1` builds the same zip without GitHub Actions:

```powershell
pwsh tools/package-release.ps1 -Version 0.1.0
```

It publishes the server **self-contained** and writes
`dist\windows-commander-mcp-v<version>-win-x64.zip`. For a smaller package
that requires the .NET 10 Desktop Runtime on the host, pass
`-SelfContained $false`.

## MCP Client Configuration

The server is a stdio MCP server: the MCP client launches the executable and
talks to it over stdin/stdout. Point your client's `.mcp.json` at the
`WindowsCommander.McpServer.exe` you unzipped from the release package, using
its full absolute path:

```json
{
  "mcpServers": {
    "windows-commander": {
      "command": "C:\\Tools\\windows-commander-mcp\\WindowsCommander.McpServer.exe",
      "args": []
    }
  }
}
```

High-risk tools (process kills, file writes/deletes, environment changes,
script execution) prompt for local confirmation by default. To disable the
prompt for unattended/automated hosts, add an `env` block:

```json
{
  "mcpServers": {
    "windows-commander": {
      "command": "C:\\Tools\\windows-commander-mcp\\WindowsCommander.McpServer.exe",
      "args": [],
      "env": { "WINDOWS_COMMANDER_UNATTENDED": "1" }
    }
  }
}
```

## Build from source

For development, publish directly instead of packaging:

```powershell
dotnet publish src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release -r win-x64
```

The generated executable is:

```text
src\WindowsCommander.McpServer\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\WindowsCommander.McpServer.exe
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
