# Windows Commander MCP

Windows Commander MCP is a `.NET 10` stdio MCP server that gives MCP clients
(Claude Code, Claude Desktop, and other compatible hosts) hands-on control of
a Windows desktop: windows, input, screen capture, UI Automation, processes,
registry, services, files, clipboard, and shell.

## Status

Stable and ready to use. The server exposes ~57 tools covering the core
Windows desktop surface, ships pre-built self-contained binaries on the
[GitHub Releases page](../../releases), and is exercised end-to-end by three
test harnesses on every change (`tools/smoke-mcp.ps1`, `tools/mutate-mcp.ps1`,
`tools/drive-mcp.ps1`).

### Capabilities

- **Process & window orchestration** — discovery, search, focus, move/resize,
  state management, child-window enumeration, foreground-activation that
  survives the Windows 11 background-activation revert.
- **Hardware input simulation** — mouse, keyboard, hotkeys, scroll, cursor
  position, scripted input sequences.
- **Reliable text entry** — `type_text` uses clipboard paste (preserving all
  original clipboard formats) instead of `SendInput`, which drops and repeats
  characters past ~12 events.
- **Vision** — full-screen, per-monitor, and region screen capture; on-device
  OCR via `Windows.Media.Ocr` (reads pixels, not just UIA metadata); visual
  candidate detection.
- **UI Automation** — tree read with depth/filter controls and a `truncated`
  flag, find/invoke/set value, element details.
- **Display & screen metadata** — display metrics, screen identification,
  window-to-screen mapping, virtual-screen coordinates throughout.
- **Execution** — PowerShell and native process execution, process control,
  Windows notifications.
- **System state** — clipboard (text), environment variables, system info,
  Windows services, registry reads, installed apps + launching.
- **File system & shell** — list, read, write, copy/move/delete, file
  properties with optional SHA256/SHA1/MD5 hashes, open paths, show in
  Explorer, search files.
- **Safety layer** — local Win32 confirmation dialog for high-risk tools,
  per-monitor pulsing border indicator, audio cue, activity-chip queue, and
  an in-memory audit log with sensitive-argument redaction.

See [`docs/tool-coverage.md`](docs/tool-coverage.md) for the full per-tool
list.

## Requirements

- Windows 10/11 interactive desktop session.
- .NET 10 Desktop Runtime (already bundled with the self-contained release
  package; only required as a separate install if you build from source or
  use the non-self-contained package).
- PowerShell 7+ available as `pwsh.exe` for `execute_powershell`.

Some tools need an interactive desktop session. Elevated target applications
may not be controllable from a non-elevated server process.

## Install (release package)

Each tagged release publishes a ready-to-run, self-contained package on the
[GitHub Releases page](../../releases) — no .NET runtime or build tools
needed on the target machine.

1. Download `windows-commander-mcp-v<version>-win-x64.zip` from the latest
   release.
2. (Optional) Verify it against the published `.sha256` file:
   `(Get-FileHash <zip> -Algorithm SHA256).Hash`.
3. Unzip it to a stable folder, e.g. `C:\Tools\windows-commander-mcp`.
4. Point your MCP client's `.mcp.json` at the `WindowsCommander.McpServer.exe`
   at the root of the unzipped folder (see [MCP Client Configuration](#mcp-client-configuration)).

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
prompt for unattended or automated hosts, add an `env` block:

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

## Safety Model

The safety layer is implemented and enabled by default.

- **Risk classification** — `RiskPolicyService` marks destructive or
  command-execution tools as high risk; `ToolDispatcher` consults it on
  every call.
- **Confirmation dialog** — high-risk tools (process kills, file writes/
  deletes, environment changes, script execution) block on a local Win32
  confirmation dialog until the local user answers; a timeout counts as
  denied. Set `WINDOWS_COMMANDER_UNATTENDED=1` to bypass for automated hosts.
- **Visual control indicators** — a per-monitor pulsing border overlay frames
  every screen during a computer-use action; high-risk actions glow orange
  with a ⚠ marker. The overlay never activates, so it cannot steal focus.
  Recent actions render as a labelled chip queue along the glow's top edge.
- **Audio cue** — short non-intrusive sound on every computer-use call.
- **Audit log** — every tool invocation is recorded in memory with its risk
  level; sensitive argument names (`password`, `token`, `secret`, `key`) are
  redacted.

See [`docs/safety.md`](docs/safety.md).

## Known Limitations

- `clipboard_access` supports the `text` format only; `html`,
  `file_drop_list`, and `image` are pending.
- `copy_move_delete_path` supports `copy`, `move`, and permanent `delete`;
  `recycle` (move to Recycle Bin) is pending.
- `get_file_properties` returns metadata, version info, and hashes but does
  not yet include alternate data stream names or a security descriptor
  summary.
- The audit log is in-memory only; persistent or exportable history is not
  yet provided.

## Repository Layout

```text
src/
  WindowsCommander.McpServer/   MCP stdio entry point and tool dispatcher
  WindowsCommander.Core/        Shared models, interfaces, operation and safety contracts
  WindowsCommander.Windows/     Windows service implementations (P/Invoke, WPF/WinForms)
  WindowsCommander.Safety/      Risk classification and audit log
  WindowsCommander.Vision/      Reserved for future vision features
tests/
  WindowsCommander.Tests/       xUnit tests
tools/
  smoke-mcp.ps1                 35 read-only tool checks
  mutate-mcp.ps1                Side-effecting tools, self-cleaning
  drive-mcp.ps1                 End-to-end typing scenario, writes PNGs to artifacts/
  package-release.ps1           Build the release zip locally
specs/
  initial_definition.md         Original target specification
```

## Build from source

```powershell
dotnet build WindowsCommander.slnx -c Release
dotnet test  WindowsCommander.slnx -c Release
dotnet publish src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release -r win-x64
```

The published executable is:

```text
src\WindowsCommander.McpServer\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\WindowsCommander.McpServer.exe
```

Note: a running server holds a lock on its own DLLs, so `dotnet publish`
fails while an MCP client has the server connected. Disconnect the server
in your MCP client (e.g. `/mcp` in Claude Code) before republishing, or use
the test harnesses below, which spawn a fresh server process each run.

## Test Harnesses

Three PowerShell scripts drive the published exe directly over stdio JSON-RPC
without involving an MCP client. Each spawns its own server and shuts it
down, so they do not require disconnecting an attached Claude Code session.

```powershell
pwsh tools/smoke-mcp.ps1    # fast: 35 read-only checks, PASS/FAIL matrix
pwsh tools/mutate-mcp.ps1   # 25 side-effecting checks, self-cleaning
pwsh tools/drive-mcp.ps1    # end-to-end typing + capture into artifacts/
```

## Run Locally (raw stdio)

```powershell
dotnet run --project src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj
```

The server communicates over line-delimited JSON-RPC via stdio.

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_system_info","arguments":{}}}
```

## Manual Validation

See [`docs/testing.md`](docs/testing.md) for manual test cases.

## Design Goals

- Keep MCP tool handlers thin; keep Windows-specific APIs behind services.
- Make destructive and computer-control operations observable and auditable.
- Use Windows virtual-screen coordinates consistently for input, capture,
  and overlays.
- Prefer local Windows/.NET-compatible dependencies; no cloud OCR or vision
  services.
