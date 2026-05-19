# Upgrade to .NET 10 (LTS) — design

## Goal

Move all six projects from .NET 8 to .NET 10 (LTS), pin the SDK for
reproducible builds, and keep the existing Windows/WinRT capabilities intact.

## Decisions

| Question | Decision |
|---|---|
| Target version | .NET 10 (LTS). SDK 10.0.102 is already installed locally. |
| `global.json` | Add one, pinning the SDK 10 band with `rollForward: latestMinor`. |
| Package bumps | Runtime-aligned only — `System.ServiceProcess.ServiceController` 8.0.1 → 10.0.x. Test tooling (xunit, Test.Sdk, coverlet) left as-is. |
| Multi-targeting | No. Single in-place TFM bump — this is a single-target desktop app. |

## Approach

A straight in-place TFM bump across all six projects. .NET major upgrades are
highly source-compatible, and the Windows SDK targeting component stays pinned
at `10.0.19041.0`, so the WinRT OCR API surface (`Windows.Media.Ocr`) is
unchanged. Multi-targeting (`net8.0;net10.0`) was rejected as unnecessary
complexity for a single-deployment app.

## Components

### 1. `global.json` (new, repo root)

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
```

Pins the build to the SDK 10 band. `latestMinor` accepts the installed
10.0.102 and any future 10.0.x. Local and CI builds now agree on the SDK.

### 2. TFM bumps — all six projects

| Project | From → To |
|---|---|
| `WindowsCommander.Core` | `net8.0` → `net10.0` |
| `WindowsCommander.Safety` | `net8.0-windows` → `net10.0-windows` |
| `WindowsCommander.Windows` | `net8.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0` |
| `WindowsCommander.McpServer` | `net8.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0` |
| `WindowsCommander.Vision` | `net8.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0` |
| `WindowsCommander.Tests` | `net8.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0` |

The `10.0.19041.0` Windows SDK component is **kept** — it gates the WinRT APIs
and is not a .NET version.

### 3. Runtime-aligned package bump

`src/WindowsCommander.Windows/WindowsCommander.Windows.csproj`:
`System.ServiceProcess.ServiceController` `8.0.1` → latest `10.0.x` (exact
patch resolved during implementation). No other package changes.

### 4. Documentation

- `CLAUDE.md` — update ".NET 8 SDK" and TFM references to .NET 10.
- `README.md` — update the "Requirements" section (".NET 8 SDK" → ".NET 10 SDK").

### 5. Release-workflow plan doc

`docs/superpowers/plans/2026-05-19-github-release-workflow.md` is written but
not yet executed. Amend it so Task 4's `setup-dotnet` uses `10.0.x` and TFM
references read `net10.0`. Plan-doc edit only — no code change there.

## Verification

- `dotnet build WindowsCommander.slnx -c Release` succeeds on .NET 10.
- `dotnet test WindowsCommander.slnx -c Release` — all tests pass.
- Watch for new nullable/analyzer warnings introduced by the newer SDK.
- Optionally `pwsh tools/smoke-mcp.ps1` against a live desktop as a final
  functional check.

## Sequencing

This upgrade lands **before** the release-workflow plan is executed, so that
plan ships .NET 10 from the start.

## Out of scope (YAGNI)

- Multi-targeting.
- Test-tooling package upgrades (xunit, Microsoft.NET.Test.Sdk, coverlet).
- Bumping the Windows SDK targeting component (`10.0.19041.0`).
