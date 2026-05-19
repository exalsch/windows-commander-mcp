# GitHub Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pushing a `v*` git tag builds, tests, and publishes a self-contained, versioned Windows Commander MCP zip as a GitHub Release — and the server reports that version over MCP.

**Architecture:** The tag version flows into the build via `dotnet publish -p:Version=`. `Program.cs` reads the version back from its own assembly at runtime. The existing `tools/package-release.ps1` stays the single packaging path; a new `.github/workflows/release.yml` is a thin wrapper around it (test → package → release).

**Tech Stack:** .NET 10 (C#, WPF/WinForms, `net10.0-windows10.0.19041.0`), xUnit, PowerShell 7, GitHub Actions (`windows-latest` runner, `softprops/action-gh-release`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WindowsCommander.McpServer/ServerInfo.cs` (create) | Server identity: name + version resolved from the running assembly. Holds the pure `CleanVersion` helper. |
| `src/WindowsCommander.McpServer/Program.cs` (modify) | `initialize` response uses `ServerInfo` instead of a hardcoded string. |
| `src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj` (modify) | Baseline `<Version>` for local/dev builds. |
| `tests/WindowsCommander.Tests/WindowsCommander.Tests.csproj` (modify) | Add a project reference to `WindowsCommander.McpServer` so `ServerInfo` is testable. |
| `tests/WindowsCommander.Tests/ServerInfoTests.cs` (create) | Unit tests for `ServerInfo.CleanVersion`. |
| `tools/package-release.ps1` (modify) | Pass the version into `dotnet publish` so the packaged exe embeds it. |
| `.github/workflows/release.yml` (create) | Tag-triggered build/test/package/release workflow. |
| `README.md` (modify) | Point users at GitHub Releases as the primary install path. |

---

## Task 1: Add `ServerInfo` with a testable version resolver

**Files:**
- Create: `src/WindowsCommander.McpServer/ServerInfo.cs`
- Modify: `tests/WindowsCommander.Tests/WindowsCommander.Tests.csproj` (add project reference)
- Test: `tests/WindowsCommander.Tests/ServerInfoTests.cs`

- [ ] **Step 1: Add the project reference so the test project can see `ServerInfo`**

In `tests/WindowsCommander.Tests/WindowsCommander.Tests.csproj`, add a fourth
`ProjectReference` to the existing `ItemGroup` (the one at lines 23-27):

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\WindowsCommander.Core\WindowsCommander.Core.csproj" />
    <ProjectReference Include="..\..\src\WindowsCommander.Safety\WindowsCommander.Safety.csproj" />
    <ProjectReference Include="..\..\src\WindowsCommander.Windows\WindowsCommander.Windows.csproj" />
    <ProjectReference Include="..\..\src\WindowsCommander.McpServer\WindowsCommander.McpServer.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `tests/WindowsCommander.Tests/ServerInfoTests.cs`:

```csharp
using System;
using WindowsCommander.McpServer;

namespace WindowsCommander.Tests;

public class ServerInfoTests
{
    [Fact]
    public void CleanVersion_ReturnsPlainSemVerUnchanged()
    {
        Assert.Equal("0.2.0", ServerInfo.CleanVersion("0.2.0", null));
    }

    [Fact]
    public void CleanVersion_StripsBuildMetadata()
    {
        Assert.Equal("0.2.0", ServerInfo.CleanVersion("0.2.0+abc1234", null));
    }

    [Fact]
    public void CleanVersion_KeepsPrereleaseSuffix()
    {
        Assert.Equal("0.2.0-rc1", ServerInfo.CleanVersion("0.2.0-rc1+abc1234", null));
    }

    [Fact]
    public void CleanVersion_FallsBackToThreePartAssemblyVersion()
    {
        Assert.Equal("1.2.3", ServerInfo.CleanVersion(null, new Version(1, 2, 3, 0)));
    }

    [Fact]
    public void CleanVersion_FallsBackWhenInformationalVersionIsBlank()
    {
        Assert.Equal("0.1.0", ServerInfo.CleanVersion("", new Version(0, 1, 0, 0)));
    }

    [Fact]
    public void Version_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ServerInfo.Version));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test WindowsCommander.slnx -c Release --filter FullyQualifiedName~ServerInfoTests`
Expected: FAIL — compile error, `ServerInfo` does not exist.

- [ ] **Step 4: Create `ServerInfo`**

Create `src/WindowsCommander.McpServer/ServerInfo.cs`:

```csharp
using System;
using System.Reflection;

namespace WindowsCommander.McpServer;

/// <summary>
/// Identity the server reports in its MCP <c>initialize</c> response. The
/// version is resolved from the running assembly, so a release build (which
/// stamps the version via <c>dotnet publish -p:Version=</c>) reports the real
/// tag version rather than a hardcoded constant.
/// </summary>
public static class ServerInfo
{
    public const string Name = "windows-commander-mcp";

    /// <summary>Version reported to MCP clients, resolved once at startup.</summary>
    public static string Version { get; } = ResolveVersion();

    static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return CleanVersion(informational, assembly.GetName().Version);
    }

    /// <summary>
    /// Normalises a raw version string. Prefers the SemVer-style informational
    /// version, dropping any <c>+build</c> metadata the SDK appends; falls back
    /// to the three-part assembly version when no informational version is set.
    /// </summary>
    public static string CleanVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plus = informationalVersion.IndexOf('+');
            return plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        }

        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test WindowsCommander.slnx -c Release --filter FullyQualifiedName~ServerInfoTests`
Expected: PASS — 6 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/WindowsCommander.McpServer/ServerInfo.cs tests/WindowsCommander.Tests/ServerInfoTests.cs tests/WindowsCommander.Tests/WindowsCommander.Tests.csproj
git commit -m "Add ServerInfo with assembly-resolved version"
```

---

## Task 2: Report the resolved version in the `initialize` response

**Files:**
- Modify: `src/WindowsCommander.McpServer/Program.cs:79-80` and the `using` block
- Modify: `src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj:3-9`

- [ ] **Step 1: Add a baseline version to the csproj**

In `src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj`, add a
`<Version>` line to the first `PropertyGroup` (lines 3-9) so local/dev builds
report a sane value. CI overrides this via `-p:Version=`:

```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>0.1.0</Version>
  </PropertyGroup>
```

- [ ] **Step 2: Add the `using` for the `ServerInfo` namespace**

In `src/WindowsCommander.McpServer/Program.cs`, add this line to the `using`
block at the top of the file (after line 7, `using WindowsCommander.Windows.Services;`):

```csharp
using WindowsCommander.McpServer;
```

- [ ] **Step 3: Replace the hardcoded `serverInfo`**

In `src/WindowsCommander.McpServer/Program.cs`, change the `serverInfo` object
literal (currently lines 77-81):

```csharp
                serverInfo = new
                {
                    name = "windows-commander-mcp",
                    version = "0.1.0"
                }
```

to:

```csharp
                serverInfo = new
                {
                    name = ServerInfo.Name,
                    version = ServerInfo.Version
                }
```

- [ ] **Step 4: Build and verify the server reports the baseline version**

Run:

```pwsh
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | dotnet run --project src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj -c Release
```

Expected: the JSON response line contains `"name":"windows-commander-mcp"`
and `"version":"0.1.0"`.

- [ ] **Step 5: Run the full test suite to confirm nothing regressed**

Run: `dotnet test WindowsCommander.slnx -c Release`
Expected: PASS — all tests pass (including the 6 `ServerInfoTests`).

- [ ] **Step 6: Commit**

```bash
git add src/WindowsCommander.McpServer/Program.cs src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj
git commit -m "Report assembly-resolved server version in initialize response"
```

---

## Task 3: Inject the version into the build in `package-release.ps1`

**Files:**
- Modify: `tools/package-release.ps1:52-54`

- [ ] **Step 1: Pass the version into `dotnet publish`**

In `tools/package-release.ps1`, change the publish invocation (currently
lines 52-54):

```pwsh
$selfContainedFlag = $SelfContained.ToString().ToLowerInvariant()
Write-Host "Publishing $Configuration / $Runtime (self-contained=$selfContainedFlag)..." -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r $Runtime --self-contained $selfContainedFlag | Out-Host
```

to:

```pwsh
$selfContainedFlag = $SelfContained.ToString().ToLowerInvariant()
Write-Host "Publishing $Configuration / $Runtime v$Version (self-contained=$selfContainedFlag)..." -ForegroundColor Cyan
# -p:Version stamps the version onto the assembly so the running server
# reports it in its MCP initialize response (see ServerInfo).
dotnet publish $project -c $Configuration -r $Runtime --self-contained $selfContainedFlag "-p:Version=$Version" | Out-Host
```

- [ ] **Step 2: Run the script with a recognisable test version**

Run: `pwsh tools/package-release.ps1 -Version 9.9.9-test`
Expected: completes with "Release package created:" and writes
`dist/windows-commander-mcp-v9.9.9-test-win-x64.zip`.

- [ ] **Step 3: Verify the packaged exe reports the injected version**

Run:

```pwsh
$tmp = Join-Path $env:TEMP 'wc-verify'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive dist/windows-commander-mcp-v9.9.9-test-win-x64.zip -DestinationPath $tmp
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | & "$tmp/WindowsCommander.McpServer.exe"
```

Expected: the response line contains `"version":"9.9.9-test"`.

- [ ] **Step 4: Clean up the test artifacts**

Run:

```pwsh
Remove-Item dist/windows-commander-mcp-v9.9.9-test-win-x64.zip -Force
Remove-Item (Join-Path $env:TEMP 'wc-verify') -Recurse -Force
```

Expected: no output, both removed.

- [ ] **Step 5: Commit**

```bash
git add tools/package-release.ps1
git commit -m "Stamp version onto the published exe in package-release.ps1"
```

---

## Task 4: Add the tag-triggered release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create the workflow file**

Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    # Windows-only: the server targets net10.0-windows and uses WPF/WinForms,
    # so it cannot build on a Linux runner.
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Resolve version from tag
        id: version
        shell: pwsh
        run: |
          $version = '${{ github.ref_name }}'.TrimStart('v')
          $prerelease = if ($version -match '-') { 'true' } else { 'false' }
          "value=$version"          >> $env:GITHUB_OUTPUT
          "prerelease=$prerelease"  >> $env:GITHUB_OUTPUT
          Write-Host "Release version: $version (prerelease=$prerelease)"

      - name: Run tests (release gate)
        run: dotnet test WindowsCommander.slnx -c Release

      - name: Package release
        shell: pwsh
        run: pwsh tools/package-release.ps1 -Version ${{ steps.version.outputs.value }}

      - name: Generate SHA256 checksum
        shell: pwsh
        run: |
          $zip = "dist/windows-commander-mcp-v${{ steps.version.outputs.value }}-win-x64.zip"
          $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
          "$hash  $(Split-Path $zip -Leaf)" | Out-File "$zip.sha256" -Encoding ascii
          Write-Host "SHA256: $hash"

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          name: windows-commander-mcp ${{ steps.version.outputs.value }}
          generate_release_notes: true
          prerelease: ${{ steps.version.outputs.prerelease }}
          files: |
            dist/windows-commander-mcp-v${{ steps.version.outputs.value }}-win-x64.zip
            dist/windows-commander-mcp-v${{ steps.version.outputs.value }}-win-x64.zip.sha256
```

- [ ] **Step 2: Validate the YAML parses**

Run:

```pwsh
pwsh -Command "Get-Content .github/workflows/release.yml -Raw | Out-Null; if ($LASTEXITCODE) { throw } else { 'file readable' }"
```

Expected: prints `file readable`. (Full validation happens when the tag is
pushed; there is no local GitHub Actions runner. The executor should visually
re-check indentation against the block above.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add tag-triggered GitHub release workflow"
```

---

## Task 5: Update the README install path

**Files:**
- Modify: `README.md:104-121` (the "Release Package" section)

- [ ] **Step 1: Replace the "Release Package" section**

In `README.md`, replace the entire "Release Package" section (currently lines
104-121, from the `## Release Package` heading through the `-SelfContained
$false` paragraph) with:

```markdown
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
```

- [ ] **Step 2: Verify the section reads correctly**

Run: `pwsh -Command "Select-String -Path README.md -Pattern 'Install \(release package\)','Building a package locally'"`
Expected: both headings are found.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Document GitHub Releases as the primary install path"
```

---

## Notes for the executor

- **Test gate on the runner:** the `windows-latest` runner has a desktop
  session, so the existing xUnit tests (registry reads, file system) are
  expected to pass. If a test proves to depend on interactive UI and fails
  *only* on the runner, stop and flag it — do not silently delete or skip it.
- **No local Actions runner:** Task 4's workflow is fully verified only by
  pushing a real `v*` tag. The executor should not push a tag; that is the
  user's call. Verification before then is limited to YAML/indentation review.
- **Committing:** this repo's convention (`CLAUDE.md`) is to commit only when
  asked. The user approved implementation, which includes the per-task commits
  above. Do not `git push` or push tags unless the user asks.
