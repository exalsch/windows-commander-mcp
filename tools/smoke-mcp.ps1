<#
.SYNOPSIS
  Read-only smoke test for the windows-commander MCP server. Exercises every
  non-destructive tool in one pass and prints a compact PASS/FAIL matrix.

.DESCRIPTION
  Companion to drive-mcp.ps1. Where drive-mcp.ps1 verifies the typing scenario
  end to end, this script answers a different question fast: "which tools are
  broken right now?" It spawns the published server, runs each safe tool once,
  and reports one line per tool. It never holds the file lock (own server
  instance, shut down each run), so Claude Code need not be disconnected.

  Exit code is 0 only if every tool passed. Destructive tools (manage_process,
  write_file, copy_move_delete_path, set_environment_variable, keyboard/mouse
  injection, focus stealing) are intentionally NOT exercised here -- use
  drive-mcp.ps1 for the interactive scenario.

.PARAMETER ServerExe
  Path to the published server exe. Defaults to the Release win-x64 publish dir.
#>
[CmdletBinding()]
param(
    [string]$ServerExe = "$PSScriptRoot\..\src\WindowsCommander.McpServer\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\WindowsCommander.McpServer.exe"
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

# --- result tracking ------------------------------------------------------
$script:results = [System.Collections.Generic.List[object]]::new()

function Test-Tool {
    # Calls one tool, records PASS/FAIL plus a short summary of the result.
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
            $txt = ($resp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
            $detail = "tool error: $txt"
        }
        else {
            $txt = ($resp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
            $img = $resp.result.content | Where-Object { $_.type -eq 'image' } | Select-Object -First 1
            if ($img) { $detail = "image $($img.data.Length) b64 chars" }
            elseif ($txt) { $detail = ($txt -replace '\s+', ' ').Trim() }
            if ($Check) {
                $parsed = $null
                if ($txt) { try { $parsed = $txt | ConvertFrom-Json } catch { } }
                $ok = & $Check $parsed $txt
                if (-not $ok) { $status = 'FAIL'; $detail = "check failed: $detail" }
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

# --- run ------------------------------------------------------------------
if (-not (Test-Path $ServerExe)) { throw "Server exe not found: $ServerExe. Publish first." }

Write-Host "Resetting Notepad for window-scoped tools..." -ForegroundColor Cyan
# Launch Microsoft Notepad by explicit path. 'notepad' on PATH can resolve to a
# Git/MSYS shim or App Execution Alias, which yields no class 'Notepad' window.
Get-Process -Name 'Notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Start-Process "$env:SystemRoot\System32\notepad.exe"
Start-Sleep -Milliseconds 2000

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$proc = Start-Server -Exe $ServerExe
try {
    $init = Invoke-Rpc -Proc $proc -Method 'initialize' -Params @{
        protocolVersion = '2024-11-05'; capabilities = @{}
        clientInfo = @{ name = 'smoke-mcp'; version = '1.0' }
    }
    Send-Notification -Proc $proc -Method 'notifications/initialized'
    Write-Host "Server up, protocol $($init.result.protocolVersion)`n" -ForegroundColor Cyan

    # Resolve a Notepad handle for window-scoped tools. class_name is matched
    # exactly by the server, so 'Notepad' never collides with 'Notepad++'.
    $waitResp = Invoke-Rpc -Proc $proc -Method 'tools/call' -Params @{
        name = 'wait_for_window'; arguments = @{ class_name = 'Notepad'; timeout_ms = 15000 }
    }
    $hwnd = $null; $npPid = $null
    $waitTxt = ($waitResp.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
    if ($waitTxt) {
        try { $w = $waitTxt | ConvertFrom-Json; $hwnd = $w.hwnd; $npPid = $w.owningPID } catch { }
    }
    if (-not $hwnd) {
        Write-Host "WARN: Notepad not resolved. wait_for_window said: $waitTxt" -ForegroundColor Yellow
        Write-Host "WARN: UI-automation, vision and child-window tools will be skipped.`n" -ForegroundColor Yellow
    } else {
        Write-Host "Notepad handle: $hwnd  pid: $npPid`n" -ForegroundColor Cyan
    }

    Write-Host "--- system / discovery -------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'get_system_info'    @{}                                    { param($p) $null -ne $p } | Out-Null
    Test-Tool $proc 'get_display_metrics' @{}                                   { param($p) $p.Count -ge 1 } | Out-Null
    Test-Tool $proc 'get_screen_details'  @{}                                                                | Out-Null
    Test-Tool $proc 'identify_screens'    @{}                                                                | Out-Null
    Test-Tool $proc 'get_screen_at_point' @{ x = 0; y = 0 }                                                  | Out-Null
    Test-Tool $proc 'get_operation_history' @{ limit = 5 }                                                   | Out-Null

    Write-Host "--- processes / windows ------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'list_processes' @{ sort_by_memory = $true }                                             | Out-Null
    Test-Tool $proc 'list_windows'   @{}                                                                     | Out-Null
    Test-Tool $proc 'find_window'    @{ class_name = 'Notepad'; visible_only = $false }                       | Out-Null
    Test-Tool $proc 'get_cursor_position' @{}                                                                | Out-Null
    if ($npPid) {
        Test-Tool $proc 'get_process_details' @{ pid = [int]$npPid }                                         | Out-Null
    }
    if ($hwnd) {
        Test-Tool $proc 'enumerate_child_windows' @{ window_handle = $hwnd }                                 | Out-Null
        Test-Tool $proc 'get_window_screen_info'  @{ window_handle = $hwnd }                                 | Out-Null
    }

    Write-Host "--- UI automation ------------------------------------------" -ForegroundColor Yellow
    if ($hwnd) {
        # read_ui_tree returns { elements, truncated, maxDepth, elementCount }.
        $rt = Test-Tool $proc 'read_ui_tree' @{ hwnd = $hwnd } `
            { param($p) $p -and $null -ne $p.elements -and $p.elementCount -ge 1 }
        $rtTxt = ($rt.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).text
        $editRef = $null
        if ($rtTxt) {
            try {
                $tree = ($rtTxt | ConvertFrom-Json).elements
                $types = ($tree | Group-Object controlType | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ' '
                Write-Host "  ui tree: $($tree.Count) elements -> $types" -ForegroundColor DarkGray
                $edit = $tree | Where-Object { $_.controlType -in @('Edit', 'Document', 'Text') -and $_.bounds.width -gt 0 } | Select-Object -First 1
                if ($edit) { $editRef = $edit.elementRef }
            } catch { }
        }
        # Notepad's editing surface is a 'Document' control, not 'Edit'.
        Test-Tool $proc 'find_ui_element' @{ hwnd = $hwnd; control_type = 'Document' } `
            { param($p) $p -and @($p).Count -ge 1 } | Out-Null
        if ($editRef) {
            Test-Tool $proc 'get_ui_element_details' @{ element_ref = $editRef }                             | Out-Null
        } else {
            Write-Host "  get_ui_element_details     SKIP no editable element in tree" -ForegroundColor DarkGray
        }
    }

    Write-Host "--- vision --------------------------------------------------" -ForegroundColor Yellow
    if ($hwnd) {
        Test-Tool $proc 'ocr_screen'             @{ target = 'window'; hwnd = $hwnd }                        | Out-Null
        Test-Tool $proc 'detect_visual_elements' @{ target = 'window'; hwnd = $hwnd }                        | Out-Null
    }

    Write-Host "--- files / shell ------------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'list_directory'      @{ path = $repoRoot }                                              | Out-Null
    Test-Tool $proc 'read_file'           @{ path = "$repoRoot\README.md"; max_bytes = 4096 }                | Out-Null
    Test-Tool $proc 'get_file_properties' @{ path = "$repoRoot\README.md"; hash_algorithm = 'SHA256' }       | Out-Null
    Test-Tool $proc 'search_files'        @{ roots = @($repoRoot); name_pattern = '*.slnx'; max_results = 5 }| Out-Null

    Write-Host "--- environment / registry / clipboard ---------------------" -ForegroundColor Yellow
    Test-Tool $proc 'get_environment_variable' @{ name = 'PATH'; scope = 'process' }                         | Out-Null
    Test-Tool $proc 'read_registry'   @{ hive = 'HKLM'; key_path = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion'; value_name = 'ProductName' } | Out-Null
    Test-Tool $proc 'clipboard_access' @{ action = 'read' }                                                  | Out-Null

    Write-Host "--- services / apps ----------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'list_services'      @{ status_filter = 'running' }                                      | Out-Null
    Test-Tool $proc 'list_installed_apps' @{ name_filter = 'Microsoft' }                                     | Out-Null

    Write-Host "--- indicators ---------------------------------------------" -ForegroundColor Yellow
    Test-Tool $proc 'get_control_indicator_status' @{}                                                       | Out-Null

    Write-Host "--- agent-efficiency features ------------------------------" -ForegroundColor Yellow
    # process_name_exact: exact match must never return a different executable
    # (e.g. 'Notepad' must not also yield 'notepad++').
    Test-Tool $proc 'find_window' @{ process_name = 'Notepad'; process_name_exact = $true; visible_only = $false } `
        -Label 'process_name_exact' `
        { param($p) ($null -eq $p) -or (@($p) | Where-Object { $_.processName -and $_.processName -ne 'Notepad' }).Count -eq 0 } | Out-Null
    if ($hwnd) {
        # read_ui_tree max_depth: depth 1 must report truncated for Notepad's
        # nested tree; depth 5 must surface strictly more elements.
        $d1 = Test-Tool $proc 'read_ui_tree' @{ hwnd = $hwnd; max_depth = 1 } -Label 'max_depth=1' `
            { param($p) $p.maxDepth -eq 1 -and $p.truncated -eq $true }
        $d1Count = 0
        try { $d1Count = (($d1.result.content | Where-Object type -eq 'text' | Select-Object -First 1).text | ConvertFrom-Json).elementCount } catch { }
        $full = Test-Tool $proc 'read_ui_tree' @{ hwnd = $hwnd; max_depth = 5 } -Label 'max_depth=5' `
            { param($p) $p.maxDepth -eq 5 -and $p.elementCount -gt $d1Count }
        $fullCount = 0
        try { $fullCount = (($full.result.content | Where-Object type -eq 'text' | Select-Object -First 1).text | ConvertFrom-Json).elementCount } catch { }
        # control_types filter: every returned element is the requested type.
        Test-Tool $proc 'read_ui_tree' @{ hwnd = $hwnd; max_depth = 5; control_types = @('Button') } -Label 'control_types' `
            { param($p) $p.elementCount -ge 1 -and (@($p.elements) | Where-Object { $_.controlType -ne 'Button' }).Count -eq 0 } | Out-Null
        # interactable_only must drop structural noise -> fewer than the full tree.
        Test-Tool $proc 'read_ui_tree' @{ hwnd = $hwnd; max_depth = 5; interactable_only = $true } -Label 'interactable_only' `
            { param($p) $p.elementCount -ge 1 -and $p.elementCount -lt $fullCount } | Out-Null
    }
    # capture_screen single-monitor targets: region must be one monitor's
    # bounds, not the full multi-monitor virtual screen.
    Test-Tool $proc 'capture_screen' @{ target = 'primary_screen' } -Label 'primary_screen' `
        { param($p) $p.region.width -gt 0 -and $p.region.height -gt 0 } | Out-Null
    Test-Tool $proc 'capture_screen' @{ target = 'screen-1' } -Label 'screen-1' `
        { param($p) $p.region.width -gt 0 -and $p.region.height -gt 0 } | Out-Null

    # --- summary ----------------------------------------------------------
    $pass = ($script:results | Where-Object Status -eq 'PASS').Count
    $fail = ($script:results | Where-Object Status -eq 'FAIL').Count
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ("SMOKE: {0} passed, {1} failed, {2} total" -f $pass, $fail, $script:results.Count) `
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
}
exit $(if (($script:results | Where-Object Status -eq 'FAIL').Count -eq 0) { 0 } else { 1 })
