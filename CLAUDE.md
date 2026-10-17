# windows-commander-mcp — development guide

A Model Context Protocol (MCP) server that gives an MCP client (Claude Code,
Claude Desktop) hands-on control of a Windows desktop: windows, input, screen
capture, UI Automation, processes, registry, services, files, clipboard.

This file is the pick-up point for continuing development — especially on a
Windows VM, where the agent can drive the VM's desktop while the developer
works on the host.

## Prerequisites (set up once on the VM)

- Windows 10/11.
- **.NET 10 SDK** (the projects target `net10.0-windows10.0.19041.0`; WPF +
  WinForms are used, so the .NET 10 *Desktop* runtime is required — the SDK
  includes it. The Windows 10 SDK version on the TFM unlocks the WinRT APIs,
  e.g. `Windows.Media.Ocr` for `ocr_screen`). `global.json` pins the build to
  the SDK 10.x band.
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
| `WindowsCommander.McpServer` | The stdio JSON-RPC MCP server; `ToolDispatcher` maps ~60 tools |
| `tests/WindowsCommander.Tests` | xUnit tests |

## Build, test, publish

```pwsh
dotnet build WindowsCommander.slnx -c Release          # compile everything
dotnet test  WindowsCommander.slnx -c Release          # run xUnit tests
dotnet publish src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release -r win-x64
```

Published server exe (this is what `.mcp.json` launches):

```
src/WindowsCommander.McpServer/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/WindowsCommander.McpServer.exe
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

- **`smoke-mcp.ps1`** — runs every *read-only* tool once (35 checks) and prints
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
  glow frames **one window per monitor** (each screen sized to its own
  resolution); a capture tool instead frames **just what it captures** — the
  captured screen, or the captured window's border (`SignalActivity`'s
  `bounds`, resolved by `IVisionService.ResolveCaptureGlowBounds` /
  `ResolveGlowBounds`). A `full_screen` capture, or any unresolvable target,
  falls back to framing every monitor. It never activates
  (`ShowActivated = false`) so it cannot steal focus. It settles to a faint idle
  border 2 s after the last call and hides after a further 3 s idle (see
  *Activity-chip queue* in Status).
- **WPF/WinForms threading**: clipboard and UI overlays need STA threads;
  see `RunOnStaThread` and the overlay dispatcher thread.

## Status — done this far

- MCP protocol correctness, UTF-8 streams, `IntPtr` converter.
- Full JSON input schemas for all ~60 tools.
- `capture_screen` returns an MCP `image` content type, downscaled.
- `focus_window` robust foreground activation (verified).
- `type_text` via clipboard paste, all-formats clipboard preservation (verified).
- Computer-use sound + per-monitor glowing border indicator; capture tools
  frame only the captured screen / window.
- **Risk gating**: `RiskPolicyService` is wired into `ToolDispatcher`. High-risk
  tools (process kills, file writes/deletes, env changes, script execution)
  require a local confirmation dialog before they run; the audit log records
  the risk level and any blocked attempt. See *Safety / unattended mode* below.
- **Indicator phases**: the activity glow shows a bright pulse during an
  action, settles to a faint persistent border for 3 s (session still
  connected), then hides. High-risk actions glow orange with a "⚠" label.
- **Activity-chip queue**: recent actions render as a horizontal queue of
  labelled chips ("⚡ typing text") along the glow's top edge, newest
  highlighted, capped at 5. Because each action is its own chip, a screenshot
  caught mid-render shows fewer chips rather than one label misattributed to
  the wrong action.
- `smoke-mcp.ps1`: 35 read-only checks verified against a live desktop.
- `mutate-mcp.ps1`: mutating checks verified (window state, input
  injection, UI Automation actions, file writes, env vars, process control),
  plus a capture *bring-to-front* check: a minimized target is raised before a
  default `capture_screen`, and stays minimized under `bring_to_front: false`.

## Safety / unattended mode

High-risk tools are gated behind a local Win32 confirmation dialog. The
dialog blocks the tool call until the local user answers (or it times out,
which counts as denied).

Automated runs cannot answer a dialog, so the server reads
`WINDOWS_COMMANDER_UNATTENDED` — set it to `1`/`true` to disable gating. All
three `tools/*.ps1` harnesses set it on the server process they spawn. A real
Claude Code session runs **gated** unless you add the variable to `.mcp.json`'s
`env` block.

## Next to test / verify

`smoke-mcp.ps1` + `mutate-mcp.ps1` cover the tool surface non-error against a
live desktop. Still worth deeper verification:

- **Visual correctness** — the harnesses assert results are non-error and
  shape-correct, but most do not pixel-verify. Open `artifacts/*.png` for
  `type_text`; spot-check `move_resize_window`, `set_window_state`, OCR.
- **Risk-gating dialog** — the *attended* path (a real high-risk call popping
  the confirmation dialog) cannot be harness-tested; verify it manually. The
  classification table is unit-tested and the unattended bypass is covered.
- **`launch_app`** non-path identifiers: `shell_uri`, `aumid`, `shortcut_name`
  (only `path` is exercised).
- **`open_path`** and `show_in_explorer` (intrusive — open windows; not in the
  harnesses).

## Known schema / agent-ergonomics notes

These were tightened to reduce wasted agent round-trips; they are regression-
covered by `smoke-mcp.ps1`'s "agent-efficiency features" section:

- `find_ui_element` / `read_ui_tree`: `control_type` is matched **exactly**
  against UIA programmatic names and is a schema `enum`. Notepad and most
  multi-line editors expose their text surface as `Document`, not `Edit`.
- `read_ui_tree` takes an optional `max_depth` (1–20, default 5) and returns
  `{ elements, truncated, maxDepth, elementCount }` — `truncated` tells the
  caller the tree was cut so it can re-request deeper. `find_ui_element` also
  accepts `max_depth`. To shrink the payload, `read_ui_tree` also takes
  `control_types` (keep only listed control types) and `interactable_only`
  (keep only elements with an actionable pattern); `truncated` is still
  computed over the full tree.
- `ocr_screen` runs real on-device OCR (`Windows.Media.Ocr`) over the captured
  pixels — it reads text drawn on screen, not just UIA metadata. One block per
  recognised line, bounds in virtual-screen coordinates.
- `find_window` / `wait_for_window`: `class_name` is matched exactly;
  `process_name` is a **substring** match by default (`"Notepad"` also matches
  `notepad++`) — pass `process_name_exact: true` for a whole-string match.
- `capture_screen` `target`: a multi-monitor `full_screen` capture downscales
  to an unreadable blur. Use `primary_screen` or `screen-N` (1-based, matches
  the `monitorId` scheme) to capture one monitor; `active_window` resolves the
  real foreground window.
- `capture_screen` / `ocr_screen` `bring_to_front` (default `true`): when the
  target names a *specific window* (a numeric `target` or an explicit `hwnd`),
  the dispatcher raises that window to the top of the Z order (via
  `WindowService.RaiseWindowForCapture`, then a short settle delay) before the
  pixels are read — otherwise `CopyFromScreen` photographs whatever sits on top
  of an occluded/minimized target. This is a **non-activating** raise
  (`SetWindowPos` `HWND_TOP` + `SWP_NOACTIVATE`; a minimized target is restored
  with `SW_SHOWNOACTIVATE`): it re-stacks the window for the grab but does **not**
  steal keyboard focus from whatever the user is working in — deliberately the
  opposite of `WindowService.ForceForeground`. Using `FocusWindow` here (as an
  earlier version did) yanked focus to the captured window, which the user saw
  as "the terminal grabs focus" when a capture targeted it. No effect for screen
  targets or `active_window` (already foreground); `TryResolveCaptureWindowHandle`
  returns null for those. Pass `bring_to_front: false` to read a background
  window without disturbing z-order. `mutate-mcp.ps1` covers both paths.

## Focus and user prompts

- `get_foreground_window`: the window that currently receives keyboard input
  (`GetForegroundWindow`), as a `WindowDetails`; `{ "window": null }` when
  there is none. While the session is locked it reports `LockApp`'s
  "Windows Default Lock Screen" window.
- `get_focused_ui_element`: UIA `AutomationElement.FocusedElement` with an
  `element_ref`, its process, and the top-level window (nearest ancestor with
  an HWND, then `GetAncestor(GA_ROOT)`). Use it before `type_text` to check
  where the text lands.
- `ask_user`: a WinForms dialog (`UserPromptForm`) with up to 6 option buttons
  and an optional text field. Returns `decision` (`answered` / `cancelled` /
  `timeout`), `choice` and `text`. Unlike `request_user_confirmation` (a
  `MessageBox` that stays open after its timeout), the form closes itself when
  the timeout elapses. Not risk-gated. `mutate-mcp.ps1` covers the timeout
  path (and checks the dialog is gone) and the answered path, where a second
  server instance types into the field and clicks a button via UIA.

## Conventions

- C# top-level statements, records, nullable enabled. Match surrounding style.
- Keep platform P/Invoke in `WindowsCommander.Windows/Native/NativeMethods.cs`.
- New tools: implement the service + interface, add a case and a full JSON
  schema in `ToolDispatcher`, and add it to `ComputerUseTools` if it drives
  the desktop.
- Git branch: `main`. Commit/push only when asked.
