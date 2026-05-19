<#
.SYNOPSIS
  Builds a distributable release package (zip) of the windows-commander MCP
  server.

.DESCRIPTION
  Publishes the MCP server and zips the publish output into
  dist/windows-commander-mcp-v<Version>-<Runtime>.zip.

  By default the publish is self-contained: the .NET runtime is bundled, so
  the target machine needs nothing pre-installed. A user unzips the package
  to any folder and points their MCP client at WindowsCommander.McpServer.exe.

  A running server locks its own DLLs, so any live instance is stopped before
  publishing — you do not need to disconnect Claude Code by hand.

.PARAMETER Version
  Version string used in the zip file name. Defaults to 0.1.0 (the version the
  server reports in its MCP initialize response).

.PARAMETER Configuration
  Build configuration. Defaults to Release.

.PARAMETER Runtime
  .NET runtime identifier to publish for. Defaults to win-x64.

.PARAMETER SelfContained
  Bundle the .NET runtime into the package. Defaults to true. Set to $false
  for a smaller package that requires the .NET 10 Desktop Runtime on the host.

.EXAMPLE
  pwsh tools/package-release.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$project = Join-Path $repoRoot 'src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj'
$distDir = Join-Path $repoRoot 'dist'

# A running server locks its own DLLs; stop any instance before publishing.
Get-Process -Name 'WindowsCommander.McpServer' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$selfContainedFlag = $SelfContained.ToString().ToLowerInvariant()
Write-Host "Publishing $Configuration / $Runtime v$Version (self-contained=$selfContainedFlag)..." -ForegroundColor Cyan
# -p:Version stamps the version onto the assembly so the running server
# reports it in its MCP initialize response (see ServerInfo).
dotnet publish $project -c $Configuration -r $Runtime --self-contained $selfContainedFlag "-p:Version=$Version" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# Locate the publish folder. The target-framework segment of the path is read
# from the build output rather than hard-coded, so a TFM bump needs no edit.
$publishDir = Get-ChildItem -Path (Join-Path $repoRoot 'src/WindowsCommander.McpServer/bin') -Recurse -Directory -Filter 'publish' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match [regex]::Escape("\$Runtime\") } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $publishDir) { throw "Publish output folder not found under bin/ for runtime '$Runtime'." }

$exe = Join-Path $publishDir.FullName 'WindowsCommander.McpServer.exe'
if (-not (Test-Path $exe)) { throw "Published executable not found: $exe" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir "windows-commander-mcp-v$Version-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Packaging -> $zipPath" -ForegroundColor Cyan
# Zip the publish folder's contents (not the folder itself) so the exe sits at
# the root of the archive.
Compress-Archive -Path (Join-Path $publishDir.FullName '*') -DestinationPath $zipPath

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Release package created:" -ForegroundColor Green
Write-Host "  $zipPath  ($sizeMB MB)"
Write-Host ""
Write-Host "Install: unzip it to a stable folder, then point your MCP client at" -ForegroundColor Yellow
Write-Host "WindowsCommander.McpServer.exe, e.g. .mcp.json:" -ForegroundColor Yellow
Write-Host @'
{
  "mcpServers": {
    "windows-commander": {
      "command": "C:\\Tools\\windows-commander-mcp\\WindowsCommander.McpServer.exe",
      "args": []
    }
  }
}
'@
