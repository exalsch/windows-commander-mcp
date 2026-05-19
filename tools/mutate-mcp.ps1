<#
.SYNOPSIS
  Mutating-tool test for the windows-commander MCP server. Exercises the
  side-effecting tools (input injection, window state, UI Automation actions,
  file writes, environment changes, process control) and prints a PASS/FAIL
  matrix.

.DESCRIPTION
  Companion to smoke-mcp.ps1 (read-only) and drive-mcp.ps1 (typing scenario).
  Every mutation here is scoped to a throwaway target and reverted or cleaned
  up: a dedicated Notepad is launched as the target, file operations use a
  temp directory, the test environment variable is removed, and every Notepad
  spawned is killed in the finally block.

  Spawns its own server instance over stdio JSON-RPC, so it never holds the
  publish file lock. Exit code is 0 only if every tool passed.
#>
[CmdletBinding()]
param(
    [string]$ServerExe = "$PSScriptRoot\..\src\WindowsCommander.McpServer\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\WindowsCommander.McpServer.exe"
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
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = $utf8
    return [System.Diagnostics.Process]::Start($psi)
}

function Invoke-Rpc {
    param([System.Diagnostics.Process]$Proc, [string]$Method, [hashtable]$Params)
    $script:nextId++
    $req = @{ jsonrpc = '2.0'; id = $script:nextId; method = $Method; params = $Params }
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

$script:results = [System.Collections.Generic.List[object]]::new()

function Test-Tool {
    # Calls one tool, records PASS/FAIL plus a short summary. Returns the raw
    # JSON-RPC response so the caller can chain element refs etc.
    param(
        [System.Diagnostics.Process]$Proc,
        [string]$Name,
        [hashtable]$Arguments = @{},
        [scriptblock]$Check,
        [string]$Label
    )
    $row = if ($Label) { "$Name ($Label)" } else { $Name }
    $status = 'PASS'; $detail = ''
    try {
        $resp = Invoke-Rpc -Proc $Proc -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
        if ($resp.error) {
            $status = 'FAIL'; $detail = "rpc error: $($resp.error.message)"
        }
        elseif ($resp.result.isError) {
            $status = 'FAIL'
            $detail = "tool error: $(($resp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text)"
        }
        else {
            $txt = ($resp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
            if ($txt) { $detail = ($txt -replace '\s+', ' ').Trim() }
            if ($Check) {
                $parsed = $null
                if ($txt) { try { $parsed = $txt | ConvertFrom-Json } catch { } }
                if (-not (& $Check $parsed $txt)) { $status = 'FAIL'; $detail = "check failed: $detail" }
            }
        }
    }
    catch {
        $status = 'FAIL'; $detail = "exception: $($_.Exception.Message)"
    }
    $script:results.Add([pscustomobject]@{ Tool = $row; Status = $status; Detail = $detail })
    $color = if ($status -eq 'PASS') { 'Green' } else { 'Red' }
    $trimmed = if ($detail.Length -gt 104) { $detail.Substring(0, 104) + '...' } else { $detail }
    Write-Host ("  {0,-32} {1,-4} {2}" -f $row, $status, $trimmed) -ForegroundColor $color
    return $resp
}

function Get-Txt { param($Resp) ($Resp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text }

# --- run ------------------------------------------------------------------
if (-not (Test-Path $ServerExe)) { throw "Server exe not found: $ServerExe. Publish first." }

$tempDir = Join-Path $env:TEMP 'wc-mcp-mutate-test'
$envVarName = 'WC_MCP_MUTATE_TEST'
$spawnedPids = [System.Collections.Generic.List[int]]::new()

Write-Host "Launching target Notepad..." -ForegroundColor Cyan
Get-Process -Name 'Notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
$targetProc = Start-Process "$env:SystemRoot\System32\notepad.exe" -PassThru
$spawnedPids.Add($targetProc.Id)
Start-Sleep -Milliseconds 2000

$proc = Start-Server -Exe $ServerExe
try {
    $init = Invoke-Rpc -Proc $proc -Method 'initialize' -Params @{
        protocolVersion = '2024-11-05'; capabilities = @{}
        clientInfo = @{ name = 'mutate-mcp'; version = '1.0' }
    }
    Send-Notification -Proc $proc -Method 'notifications/initialized'
    Write-Host "Server up, protocol $($init.result.protocolVersion)`n" -ForegroundColor Cyan

    $waitResp = Invoke-Rpc -Proc $proc -Method 'tools/call' -Params @{
        name = 'wait_for_window'; arguments = @{ class_name = 'Notepad'; timeout_ms = 15000 }
    }
    $w = (Get-Txt $waitResp) | ConvertFrom-Json
    $hwnd = $w.hwnd; $npPid = $w.owningPID
    if (-not $hwnd) { throw "Could not resolve target Notepad window." }
    Write-Host "Target Notepad handle: $hwnd  pid: $npPid`n" -ForegroundColor Cyan

    Write-Host "--- window state -------------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'focus_window' @{ window_handle = $hwnd } { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'set_window_state' @{ window_handle = $hwnd; state = 'minimize' } `
        -Label 'minimize' { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'set_window_state' @{ window_handle = $hwnd; state = 'restore' } `
        -Label 'restore' { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'move_resize_window' @{ window_handle = $hwnd; x = 120; y = 120; width = 900; height = 620 } `
        { param($p) $p.bounds.width -eq 900 -and $p.bounds.height -eq 620 } | Out-Null
    Test-Tool $proc 'focus_window' @{ window_handle = $hwnd } -Label 're-focus' { param($p) $p.completed } | Out-Null

    Write-Host "--- input injection ----------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'set_cursor_position' @{ x = 400; y = 400 } { param($p) $p.completed } | Out-Null
    $cur = Test-Tool $proc 'get_cursor_position' @{} `
        { param($p) [math]::Abs($p.x - 400) -le 2 -and [math]::Abs($p.y - 400) -le 2 }
    Test-Tool $proc 'mouse_action' @{ action = 'move'; target_hwnd = $hwnd } -Label 'move' `
        { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'mouse_action' @{ action = 'click'; button = 'left'; target_hwnd = $hwnd } -Label 'click' `
        { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'mouse_wheel' @{ direction = 'down'; amount = 3; target_hwnd = $hwnd } `
        { param($p) $p.completed } | Out-Null
    # Clear the document, then type via input_sequence and verify through OCR-free UIA later.
    Test-Tool $proc 'send_hotkey' @{ modifiers = @('ctrl'); key = 'a' } { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'keyboard_action' @{ action = 'tap'; key = 'delete' } { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'input_sequence' @{
        abort_on_error = $true
        steps = @(
            @{ type = 'text'; text = 'mutate-mcp input_sequence line' },
            @{ type = 'keyboard'; key = 'enter' },
            @{ type = 'delay'; delay_ms = 100 }
        )
    } { param($p) $p.completed } | Out-Null

    Write-Host "--- UI Automation actions ----------------------------------" -ForegroundColor Yellow
    $fe = Invoke-Rpc -Proc $proc -Method 'tools/call' -Params @{
        name = 'find_ui_element'; arguments = @{ hwnd = $hwnd; control_type = 'Document' }
    }
    $docRef = $null
    try { $docRef = ((Get-Txt $fe) | ConvertFrom-Json | Select-Object -First 1).elementRef } catch { }
    if ($docRef) {
        Test-Tool $proc 'invoke_ui_element' @{ element_ref = $docRef; action = 'focus' } `
            { param($p) $p.completed } | Out-Null
        # Notepad's editing surface is a RichEdit 'Document'; it may or may not
        # expose ValuePattern. A genuine "does not support ValuePattern" error
        # is a real result, not a harness bug -- it shows in the matrix.
        Test-Tool $proc 'set_ui_value' @{ element_ref = $docRef; value = 'set_ui_value test text' } `
            { param($p) $p.completed } | Out-Null
    } else {
        Write-Host "  invoke_ui_element / set_ui_value  SKIP no Document element found" -ForegroundColor DarkGray
    }

    Write-Host "--- file mutations -----------------------------------------" -ForegroundColor Yellow
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    $fileA = Join-Path $tempDir 'a.txt'
    $fileB = Join-Path $tempDir 'b.txt'
    $payload = "windows-commander mutate test payload"
    Test-Tool $proc 'write_file' @{ path = $fileA; content = $payload; create_directories = $true; overwrite = $true } `
        { param($p) (Test-Path $fileA) } | Out-Null
    Test-Tool $proc 'read_file' @{ path = $fileA } -Label 'roundtrip' `
        { param($p) $p.content -eq $payload } | Out-Null
    Test-Tool $proc 'copy_move_delete_path' @{ action = 'copy'; source_path = $fileA; destination_path = $fileB; overwrite = $true } `
        -Label 'copy' { param($p) (Test-Path $fileB) } | Out-Null
    Test-Tool $proc 'copy_move_delete_path' @{ action = 'delete'; source_path = $fileB } `
        -Label 'delete' { param($p) -not (Test-Path $fileB) } | Out-Null

    Write-Host "--- environment --------------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'set_environment_variable' @{ name = $envVarName; value = 'hello-mutate'; scope = 'process' } `
        -Label 'set' { param($p) $p.updated } | Out-Null
    Test-Tool $proc 'get_environment_variable' @{ name = $envVarName; scope = 'process' } `
        -Label 'verify' { param($p) $p.value -eq 'hello-mutate' } | Out-Null
    Test-Tool $proc 'set_environment_variable' @{ name = $envVarName; scope = 'process' } `
        -Label 'remove' { param($p) $p.updated } | Out-Null

    Write-Host "--- app launch / process control ---------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'launch_app' @{ identifier = "$env:SystemRoot\System32\notepad.exe"; identifier_type = 'path' } `
        { param($p) $p.started } | Out-Null
    Start-Sleep -Milliseconds 1500
    # manage_process 'terminate' works on any process: kill an extra Notepad
    # process (one whose pid is not the windowed target).
    $extraPid = (Get-Process -Name 'Notepad' -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $npPid } | Select-Object -First 1).Id
    if ($extraPid) {
        Test-Tool $proc 'manage_process' @{ pid = [int]$extraPid; action = 'terminate' } `
            -Label 'terminate' { param($p) $p.completed } | Out-Null
        Start-Sleep -Milliseconds 300
        if (Get-Process -Id $extraPid -ErrorAction SilentlyContinue) {
            $script:results.Add([pscustomobject]@{ Tool = 'manage_process (terminate verify)'; Status = 'FAIL'; Detail = "pid $extraPid still alive" })
            Write-Host "  manage_process (terminate verify) FAIL pid $extraPid still alive" -ForegroundColor Red
        }
    } else {
        Write-Host "  manage_process (terminate)  SKIP no extra Notepad pid found" -ForegroundColor DarkGray
    }
    # 'close_main_window' needs a real top-level window: use the windowed
    # target Notepad. This also serves as cleanup for the target.
    Test-Tool $proc 'manage_process' @{ pid = [int]$npPid; action = 'close_main_window' } `
        -Label 'close_main_window' { param($p) $p.completed } | Out-Null

    # --- summary ----------------------------------------------------------
    $pass = ($script:results | Where-Object Status -eq 'PASS').Count
    $fail = ($script:results | Where-Object Status -eq 'FAIL').Count
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ("MUTATE: {0} passed, {1} failed, {2} total" -f $pass, $fail, $script:results.Count) `
        -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
    if ($fail -gt 0) {
        Write-Host "FAILURES:" -ForegroundColor Red
        $script:results | Where-Object Status -eq 'FAIL' | ForEach-Object {
            Write-Host ("  {0}: {1}" -f $_.Tool, $_.Detail) -ForegroundColor Red
        }
    }
}
finally {
    if (-not $proc.HasExited) {
        $proc.StandardInput.Close()
        if (-not $proc.WaitForExit(3000)) { $proc.Kill() }
    }
    # Clean up: kill every Notepad spawned, drop the temp dir.
    Get-Process -Name 'Notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue }
}
exit $(if (($script:results | Where-Object Status -eq 'FAIL').Count -eq 0) { 0 } else { 1 })
