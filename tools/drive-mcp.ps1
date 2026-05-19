<#
.SYNOPSIS
  Drives the windows-commander MCP server directly over stdio JSON-RPC, with no
  dependency on Claude Code's MCP client. Lets a full computer-use scenario be
  exercised in one shot: edit -> publish -> run this -> read results.

.DESCRIPTION
  Spawns the published server exe, performs the initialize handshake, then runs
  the typing test scenario:
    1. close any stray Notepad windows, launch a fresh one
    2. find_window  -> resolve the Notepad handle
    3. focus_window -> bring it to the foreground
    4. type_text    -> inject the test string
    5. capture_screen -> save a PNG of the result for visual inspection

  Every JSON-RPC response is printed. The screenshot is written to disk so the
  caller can open it. Exit code is 0 only if every step's result is non-error.

.PARAMETER Text
  The string to type. Defaults to a Unicode/em-dash torture-test line.

.PARAMETER ServerExe
  Path to the published server exe. Defaults to the Release win-x64 publish dir.

.PARAMETER ShotPath
  Where to write the verification screenshot PNG.
#>
[CmdletBinding()]
param(
    [string]$Text = "windows-commander typing test - em-dash there & 'quotes' + symbols (){}[] ~ done.",
    [string]$ServerExe = "$PSScriptRoot\..\src\WindowsCommander.McpServer\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\WindowsCommander.McpServer.exe",
    [string]$ShotPath = "$PSScriptRoot\..\artifacts\typing-test.png",
    [string]$ShotFullPath = "$PSScriptRoot\..\artifacts\typing-test-full.png"
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$script:nextId = 0

function Start-Server {
    param([string]$Exe)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    # Harness cannot answer the high-risk confirmation dialog: run unattended.
    $psi.EnvironmentVariables["WINDOWS_COMMANDER_UNATTENDED"] = "1"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    # Stderr is left attached to the console: redirecting it would require
    # draining it on a thread, and a PowerShell event handler cannot run on the
    # threadpool callback thread. Server log lines simply interleave here.
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = $utf8
    return [System.Diagnostics.Process]::Start($psi)
}

function Invoke-Rpc {
    # Sends one request and returns the parsed response object.
    param([System.Diagnostics.Process]$Proc, [string]$Method, [hashtable]$Params)
    $script:nextId++
    $id = $script:nextId
    $req = @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Params }
    $Proc.StandardInput.WriteLine(($req | ConvertTo-Json -Depth 20 -Compress))
    $Proc.StandardInput.Flush()
    $line = $Proc.StandardOutput.ReadLine()
    if ($null -eq $line) { throw "Server closed stdout while awaiting response to '$Method'." }
    return $line | ConvertFrom-Json
}

function Send-Notification {
    param([System.Diagnostics.Process]$Proc, [string]$Method)
    $req = @{ jsonrpc = '2.0'; method = $Method; params = @{} }
    $Proc.StandardInput.WriteLine(($req | ConvertTo-Json -Depth 5 -Compress))
    $Proc.StandardInput.Flush()
}

function Invoke-Tool {
    # Calls a tool and returns the inner tool result, throwing on isError.
    param([System.Diagnostics.Process]$Proc, [string]$Name, [hashtable]$Arguments)
    $resp = Invoke-Rpc -Proc $Proc -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($resp.error) { throw "$Name -> JSON-RPC error: $($resp.error | ConvertTo-Json -Compress)" }
    $result = $resp.result
    if ($result.isError) { throw "$Name -> tool error: $($result.content | ConvertTo-Json -Depth 10 -Compress)" }
    return $result
}

function Get-TextContent {
    param($Result)
    return ($Result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
}

# --- scenario -------------------------------------------------------------
if (-not (Test-Path $ServerExe)) { throw "Server exe not found: $ServerExe. Publish first." }
New-Item -ItemType Directory -Force -Path (Split-Path $ShotPath) | Out-Null

Write-Host "1. Resetting Notepad..." -ForegroundColor Cyan
Get-Process -Name 'Notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
# Launch by explicit path: 'notepad' on PATH may resolve to a Git/MSYS shim or
# an App Execution Alias, neither of which produces a class 'Notepad' window.
Start-Process "$env:SystemRoot\System32\notepad.exe"

$proc = Start-Server -Exe $ServerExe
try {
    Write-Host "2. initialize handshake..." -ForegroundColor Cyan
    $init = Invoke-Rpc -Proc $proc -Method 'initialize' -Params @{
        protocolVersion = '2024-11-05'
        capabilities    = @{}
        clientInfo      = @{ name = 'drive-mcp'; version = '1.0' }
    }
    Write-Host "   protocolVersion: $($init.result.protocolVersion)"
    Send-Notification -Proc $proc -Method 'notifications/initialized'

    Write-Host "3. wait_for_window (class 'Notepad' = Microsoft Notepad, not Notepad++)..." -ForegroundColor Cyan
    $wait = Invoke-Tool -Proc $proc -Name 'wait_for_window' -Arguments @{ class_name = 'Notepad'; timeout_ms = 12000 }
    $window = Get-TextContent $wait | ConvertFrom-Json
    Write-Host "   $(Get-TextContent $wait)"
    $hwnd = $window.hwnd
    if (-not $hwnd) { throw "No Microsoft Notepad window found." }
    Write-Host "   handle: $hwnd"

    Write-Host "4. focus_window..." -ForegroundColor Cyan
    $focus = Invoke-Tool -Proc $proc -Name 'focus_window' -Arguments @{ window_handle = $hwnd }
    Write-Host "   $(Get-TextContent $focus)"

    Write-Host "5. clearing document (Ctrl+A, Delete)..." -ForegroundColor Cyan
    # Modern Notepad restores its previous session, so a relaunched window is
    # not blank. Clear it for an unambiguous verification.
    Invoke-Tool -Proc $proc -Name 'send_hotkey' -Arguments @{ modifiers = @('ctrl'); key = 'a' } | Out-Null
    Invoke-Tool -Proc $proc -Name 'keyboard_action' -Arguments @{ action = 'tap'; key = 'delete' } | Out-Null

    Write-Host "6. type_text..." -ForegroundColor Cyan
    $type = Invoke-Tool -Proc $proc -Name 'type_text' -Arguments @{ text = $Text }
    Write-Host "   $(Get-TextContent $type)"

    Start-Sleep -Milliseconds 400
    Write-Host "7. capture_screen (Notepad window)..." -ForegroundColor Cyan
    $shot = Invoke-Tool -Proc $proc -Name 'capture_screen' -Arguments @{ target = 'window'; hwnd = $hwnd }
    $img = $shot.content | Where-Object { $_.type -eq 'image' } | Select-Object -First 1
    if (-not $img) { throw "capture_screen returned no image content." }
    [System.IO.File]::WriteAllBytes($ShotPath, [Convert]::FromBase64String($img.data))
    Write-Host "   screenshot saved: $ShotPath ($((Get-Item $ShotPath).Length) bytes)" -ForegroundColor Green

    Write-Host "7b. ocr_screen (read the typed text back via real OCR)..." -ForegroundColor Cyan
    $ocr = Invoke-Tool -Proc $proc -Name 'ocr_screen' -Arguments @{ target = 'window'; hwnd = $hwnd }
    $ocrText = (Get-TextContent $ocr | ConvertFrom-Json).combinedText
    Write-Host "   OCR read: $($ocrText -replace '\s+', ' ')"
    if ($ocrText -notmatch 'typing test') {
        throw "OCR did not read the typed text back (expected to contain 'typing test')."
    }
    Write-Host "   OCR verified the typed text." -ForegroundColor Green

    # capture_screen_region is itself a computer-use tool, so this call
    # re-signals activity and the screen-edge glow is lit when captured. A
    # corner region at native resolution shows the glow clearly (a downscaled
    # full virtual-screen shot shrinks the 22px glow to a couple of pixels).
    Write-Host "8. capture_screen_region (virtual-screen corner, to verify activity glow)..." -ForegroundColor Cyan
    $metrics = Invoke-Tool -Proc $proc -Name 'get_display_metrics' -Arguments @{}
    $screens = Get-TextContent $metrics | ConvertFrom-Json
    $allBounds = @($screens) | ForEach-Object { $_.virtualCoordinates }
    $minX = [int](($allBounds.x | Measure-Object -Minimum).Minimum)
    $minY = [int](($allBounds.y | Measure-Object -Minimum).Minimum)
    Write-Host "   virtual top-left corner: ($minX, $minY)"
    # Let the overlay's WPF render thread catch up so the verification shot
    # shows the accumulated activity-chip queue rather than a half-rendered
    # frame. (A live human watching the screen sees the chips immediately;
    # this delay only matters for a screenshot taken microseconds later.)
    Start-Sleep -Milliseconds 700
    $region = Invoke-Tool -Proc $proc -Name 'capture_screen_region' -Arguments @{
        x = $minX; y = $minY; width = 760; height = 240
    }
    $regionImg = $region.content | Where-Object { $_.type -eq 'image' } | Select-Object -First 1
    if (-not $regionImg) { throw "capture_screen_region returned no image content." }
    [System.IO.File]::WriteAllBytes($ShotFullPath, [Convert]::FromBase64String($regionImg.data))
    Write-Host "   screenshot saved: $ShotFullPath ($((Get-Item $ShotFullPath).Length) bytes)" -ForegroundColor Green

    Write-Host ""
    Write-Host "EXPECTED: $Text" -ForegroundColor Yellow
    Write-Host "DONE - inspect the screenshot to verify the typed text." -ForegroundColor Green
}
finally {
    if (-not $proc.HasExited) {
        $proc.StandardInput.Close()
        if (-not $proc.WaitForExit(3000)) { $proc.Kill() }
    }
}
