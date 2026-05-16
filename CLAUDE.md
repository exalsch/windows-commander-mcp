# windows-commander-mcp — development guide

A Model Context Protocol (MCP) server that gives an MCP client (Claude Code,
Claude Desktop) hands-on control of a Windows desktop: windows, input, screen
capture, UI Automation, processes, registry, services, files, clipboard.

This file is the pick-up point for continuing development — especially on a
Windows VM, where the agent can drive the VM's desktop while the developer
works on the host.

## Prerequisites (set up once on the VM)

- Windows 10/11.
- **.NET 8 SDK** (the projects target `net8.0-windows`; WPF + WinForms are
  used, so the .NET 8 *Desktop* runtime is required — the SDK includes it).
- **PowerShell 7+** (`pwsh`) — used by the test harness.
- Claude Code.
- Clone the repo to a stable path and update `.mcp.json` (see below) if the
  path differs from `C:\PROGRAMMING\MCP_Servers\windows-commander-mcp`.

## Solution layout

`WindowsCommander.slnx` — six projects:

| Project | Role |
|---|---|
| `WindowsCommander.Core` | Models + service interfaces (no platform code) |
| `WindowsCommander.Safety` | Risk classification (`RiskPolicyService`) |
| `WindowsCommander.Vision` | Screen capture, OCR, visual detection |
| `WindowsCommander.Windows` | Windows service implementations (P/Invoke, WPF/WinForms) |
| `WindowsCommander.McpServer` | The stdio JSON-RPC MCP server; `ToolDispatcher` maps ~57 tools |
| `tests/WindowsCommander.Tests` | xUnit tests |

## Build, test, publish

```pwsh
dotnet build WindowsCommander.slnx -c Release          # compile everything
dotnet test  WindowsCommander.slnx -c Release          # run xUnit tests
dotnet publish src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release -r win-x64
```

Published server exe (this is what `.mcp.json` launches):

```
src/WindowsCommander.McpServer/bin/Release/net8.0-windows/win-x64/publish/WindowsCommander.McpServer.exe
```

## The connection workflow — important

`.mcp.json` (repo root, not committed) points Claude Code at the published exe.

**The running server locks its own DLLs**, so `dotnet publish` fails while
Claude Code has the server connected. To deploy a change:

1. Disconnect `windows-commander` via `/mcp` in Claude Code.
2. `dotnet publish ...` (command above).
3. Reconnect via `/mcp`.

## The test harness — preferred way to iterate

Three scripts drive the **published exe directly** over stdio JSON-RPC, with
no Claude Code MCP client involved. Each spawns its own server instance and
shuts it down each run, so none holds the file lock — you do **not** need to
disconnect Claude Code to use them. If a *stray* server process is holding the
publish lock, kill it: `Get-Process WindowsCommander.McpServer | Stop-Process`.

```pwsh
pwsh tools/smoke-mcp.ps1    # fast: which tools are broken right now? (read-only)
pwsh tools/mutate-mcp.ps1   # the side-effecting tools, self-cleaning
pwsh tools/drive-mcp.ps1    # deep: the end-to-end typing scenario
```

- **`smoke-mcp.ps1`** — runs every *read-only* tool once (31 checks) and prints
  a PASS/FAIL matrix plus a one-line summary. Best first check after any
  change. Does **not** exercise destructive tools. Exit code 0 only if all pass.
- **`mutate-mcp.ps1`** — exercises the *mutating* tools (window state, input
  injection, UI Automation actions, file writes, env vars, process control).
  Every mutation is scoped to a throwaway target and reverted/cleaned up
  (temp dir removed, test env var deleted, spawned Notepads killed).
- **`drive-mcp.ps1`** — the full scenario: reset Notepad → `wait_for_window` →
  `focus_window` → clear → `type_text` → `capture_screen` →
  `capture_screen_region`. Screenshots land in `artifacts/`.

Iterate with: **edit → publish → run harness → read the PNGs / matrix**.
Extend whichever harness covers the tool you are working on.

**Notepad-launch gotcha**: the harnesses launch Microsoft Notepad by explicit
path (`%SystemRoot%\System32\notepad.exe`). Bare `notepad` on PATH can resolve
to a Git/MSYS shim or an App Execution Alias — neither produces a window with
class `Notepad`. Resolve the window by `class_name` (`find_window`/
`wait_for_window` match `class_name` *exactly*); `process_name` is a substring
match, so `process_name: "Notepad"` also matches `notepad++`.

## Platform gotchas (already handled — don't regress these)

- **stdio encoding**: stdin/stdout are wrapped in UTF-8 streams in
  `Program.cs`. Without this the host's OEM code page corrupts non-ASCII text.
- **`nint`/`IntPtr` serialization**: window/process handles serialize via
  `NativeIntJsonConverter` (`Mcp/JsonOptions.cs`). `System.Text.Json` cannot
  serialize `IntPtr` natively.
- **MCP protocol**: id-less requests are notifications and must get no reply;
  `protocolVersion` echoes the client's; tool failures return
  `{content,isError:true}`, never a JSON-RPC error.
- **`focus_window`**: Windows 11 reverts background-initiated activation.
  `WindowService.ForceForeground` clears the foreground-lock timeout, attaches
  thread input, and retries. `GetForegroundWindow()` is the only trusted check
  (`SetForegroundWindow`'s bool lies).
- **`type_text`**: pastes via the clipboard (set text → Ctrl+V → restore).
  Synthetic keystroke injection (`SendInput`) drops/repeats characters past
  ~12 events and is *not* used. `PasteText` snapshots **all** clipboard formats
  (images, file lists) and restores them; it waits 400 ms before restoring so
  the target consumes the paste first.
- **Computer-use feedback**: `ToolDispatcher` fires a sound
  (`ComputerUseNotifier`) and a pulsing screen-edge glow
  (`ControlIndicatorService.SignalActivity`) for every computer-use tool. The
  glow draws **one window per monitor** so each screen is framed at its own
  resolution; it never activates (`ShowActivated = false`) so it cannot steal
  focus. Auto-hides 2 s after the last computer-use call.
- **WPF/WinForms threading**: clipboard and UI overlays need STA threads;
  see `RunOnStaThread` and the overlay dispatcher thread.

## Status — done this far

- MCP protocol correctness, UTF-8 streams, `IntPtr` converter.
- Full JSON input schemas for all ~57 tools.
- `capture_screen` returns an MCP `image` content type, downscaled.
- `focus_window` robust foreground activation (verified).
- `type_text` via clipboard paste, all-formats clipboard preservation (verified).
- Computer-use sound + per-monitor glowing border indicator.
- `smoke-mcp.ps1`: 31 read-only checks verified against a live desktop
  (system, processes, windows, UI Automation, vision, files, registry,
  services, apps, clipboard, indicators).
- `mutate-mcp.ps1`: 25 mutating checks verified (window state, input
  injection, UI Automation actions, file writes, env vars, process control).

## Next to test / verify

`smoke-mcp.ps1` + `mutate-mcp.ps1` cover the tool surface non-error against a
live desktop. Still worth deeper verification:

- **Visual correctness** — the harnesses assert results are non-error and
  shape-correct, but most do not pixel-verify. Open `artifacts/*.png` for
  `type_text`; spot-check `move_resize_window`, `set_window_state`, OCR.
- **`launch_app`** non-path identifiers: `shell_uri`, `aumid`, `shortcut_name`
  (only `path` is exercised).
- **`open_path`** and `show_in_explorer` (intrusive — open windows; not in the
  harnesses).
- Multi-monitor glow: confirm each screen is framed correctly.

## Known schema / agent-ergonomics notes

These were tightened to reduce wasted agent round-trips; they are regression-
covered by `smoke-mcp.ps1`'s "agent-efficiency features" section:

- `find_ui_element` / `read_ui_tree`: `control_type` is matched **exactly**
  against UIA programmatic names and is a schema `enum`. Notepad and most
  multi-line editors expose their text surface as `Document`, not `Edit`.
- `read_ui_tree` takes an optional `max_depth` (1–20, default 5) and returns
  `{ elements, truncated, maxDepth, elementCount }` — `truncated` tells the
  caller the tree was cut so it can re-request deeper. `find_ui_element` also
  accepts `max_depth`.
- `find_window` / `wait_for_window`: `class_name` is matched exactly;
  `process_name` is a **substring** match by default (`"Notepad"` also matches
  `notepad++`) — pass `process_name_exact: true` for a whole-string match.

## Conventions

- C# top-level statements, records, nullable enabled. Match surrounding style.
- Keep platform P/Invoke in `WindowsCommander.Windows/Native/NativeMethods.cs`.
- New tools: implement the service + interface, add a case and a full JSON
  schema in `ToolDispatcher`, and add it to `ComputerUseTools` if it drives
  the desktop.
- Git branch: `main`. Commit/push only when asked.
