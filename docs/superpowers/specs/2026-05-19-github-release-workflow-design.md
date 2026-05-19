# Versioned release via GitHub Actions — design

## Goal

Pushing a git tag like `v0.2.0` produces a downloadable, ready-to-run zip
attached to a GitHub Release, and the MCP server reports that same version
(`0.2.0`) in its `initialize` response.

## Decisions

| Question | Decision |
|---|---|
| Trigger | Push of a version tag `v*`. The tag is the single source of truth for the version. |
| Package variant | `win-x64` self-contained only (.NET runtime bundled; no prerequisites on the target). |
| Version source | Tag version injected into the build; `Program.cs` reads it from the assembly. |
| Test gate | Yes — `dotnet test` must pass before publish/release. |

## Approach

Reuse the existing `tools/package-release.ps1` as the single packaging path
for both local builds and CI, rather than duplicating publish/zip logic in
YAML. The workflow is a thin wrapper: test → call the script → publish a
GitHub Release. A second packaging path in YAML was rejected because the two
could drift.

## Components

### 1. `src/WindowsCommander.McpServer/Program.cs`

Replace the hardcoded `version = "0.1.0"` in the `serverInfo` block
(currently `Program.cs:80`) with a value read at runtime from the running
assembly:

- Prefer `AssemblyInformationalVersionAttribute.InformationalVersion`
  (semver-friendly; supports prerelease suffixes such as `-rc1`).
- Strip any build-metadata suffix the SDK appends (the `+<commithash>` part).
- Fall back to `Assembly.GetName().Version` if the attribute is absent.

### 2. `src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj`

Add a baseline `<Version>0.1.0</Version>` property so local/dev builds report
a sane version. CI overrides it via `-p:Version=`.

### 3. `tools/package-release.ps1`

Pass `-p:Version=$Version` into the `dotnet publish` invocation so the
*packaged executable* embeds the version. Today the script uses `-Version`
only for the zip filename; the exe itself would not reflect it. Existing
callers are unaffected (the parameter already exists and defaults to `0.1.0`).

### 4. `.github/workflows/release.yml` (new)

- **Trigger:** `push` of tags matching `v*`.
- **Runner:** `windows-latest` (the project targets `net10.0-windows...` and
  uses WPF/WinForms — it cannot build on Linux).
- **Permissions:** `contents: write` (uses the built-in `GITHUB_TOKEN`).
- **Steps:**
  1. `actions/checkout`.
  2. `actions/setup-dotnet` pinned to .NET 10.
  3. `dotnet test WindowsCommander.slnx -c Release` — the gate. A failure
     fails the job, so no release is created.
  4. Derive the version: strip the leading `v` from the tag
     (`v0.2.0` → `0.2.0`).
  5. `pwsh tools/package-release.ps1 -Version <version>` → produces
     `dist/windows-commander-mcp-v<version>-win-x64.zip`.
  6. Generate a SHA256 checksum file next to the zip.
  7. Create the GitHub Release with `softprops/action-gh-release`, attaching
     the zip and checksum file, with auto-generated release notes. Mark the
     release as **prerelease** when the tag contains a `-`
     (e.g. `v0.2.0-rc1`).

### 5. `README.md`

Update the "Release Package" section so the GitHub Releases page is the
primary install path (download zip → unzip to a stable folder → point
`.mcp.json` at `WindowsCommander.McpServer.exe`). Keep the
`package-release.ps1` instructions for local builds.

## Package contents

Single artifact per release: `windows-commander-mcp-v<version>-win-x64.zip` —
self-contained, `WindowsCommander.McpServer.exe` at the archive root, plus a
`.sha256` checksum file uploaded as a separate release asset.

## Error handling

- Test failure (step 3) fails the job; no release is created.
- A non-zero exit from `package-release.ps1` (already `throw`s on publish
  failure) fails the job.
- `softprops/action-gh-release` requires `contents: write`; without it the
  release-create step fails loudly rather than silently skipping.

## Out of scope (YAGNI)

- A PR/push CI workflow (build + test on every change).
- `win-arm64` and framework-dependent package variants.
- Code signing / Authenticode.
