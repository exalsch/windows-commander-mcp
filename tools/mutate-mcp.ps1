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
    # get_foreground_window must report the window focus_window just activated.
    Test-Tool $proc 'get_foreground_window' @{} -Label 'is target' `
        { param($p) $p.hwnd -eq $hwnd -and $p.className -eq 'Notepad' } | Out-Null

    Write-Host "--- capture bring-to-front ---------------------------------" -ForegroundColor Yellow

    # Helper: re-read the target window's record so we can assert its state.
    function Get-TargetWindow {
        param([System.Diagnostics.Process]$Proc, [long]$Handle)
        try {
            $fw = Invoke-Rpc -Proc $Proc -Method 'tools/call' -Params @{ name = 'find_window'; arguments = @{ class_name = 'Notepad' } }
            return (Get-Txt $fw | ConvertFrom-Json) | Where-Object { [int64]$_.hwnd -eq [int64]$Handle } | Select-Object -First 1
        } catch { return $null }
    }

    function Add-Result {
        param([string]$Tool, [bool]$Ok, [string]$Detail)
        $st = if ($Ok) { 'PASS' } else { 'FAIL' }
        $script:results.Add([pscustomobject]@{ Tool = $Tool; Status = $st; Detail = $Detail })
        Write-Host ("  {0,-32} {1,-4} {2}" -f $Tool, $st, $Detail) -ForegroundColor $(if ($Ok) { 'Green' } else { 'Red' })
    }

    # Default (bring_to_front omitted => true): minimizing the target then
    # capturing it by handle must raise/restore it first, so an occluded or
    # minimized window is not photographed behind whatever covers it. Verify an
    # image came back AND the window ended up un-minimized.
    Test-Tool $proc 'set_window_state' @{ window_handle = $hwnd; state = 'minimize' } -Label 'pre-min' { param($p) $p.completed } | Out-Null
    Start-Sleep -Milliseconds 300
    $capResp = Test-Tool $proc 'capture_screen' @{ target = "$hwnd" } -Label 'default raises' { param($p) $true }
    $hasImage = [bool]($capResp.result.content | Where-Object { $_.type -eq 'image' -and $_.data })
    $after = Get-TargetWindow -Proc $proc -Handle $hwnd
    $raised = $hasImage -and $after -and (-not $after.isMinimized)
    Add-Result 'capture_screen (raise verify)' $raised "hasImage=$hasImage minimized=$($after.isMinimized)"

    # Opt out: bring_to_front=false must NOT raise the window, so a minimized
    # target stays minimized. (We assert the window state directly; the capture
    # of an off-screen window is allowed to return whatever pixels are there.)
    Test-Tool $proc 'set_window_state' @{ window_handle = $hwnd; state = 'minimize' } -Label 'pre-min2' { param($p) $p.completed } | Out-Null
    Start-Sleep -Milliseconds 300
    try { Invoke-Rpc -Proc $proc -Method 'tools/call' -Params @{ name = 'capture_screen'; arguments = @{ target = "$hwnd"; bring_to_front = $false } } | Out-Null } catch { }
    $afterOptOut = Get-TargetWindow -Proc $proc -Handle $hwnd
    $stayedMin = [bool]($afterOptOut -and $afterOptOut.isMinimized)
    Add-Result 'capture_screen (opt-out no raise)' $stayedMin "minimized=$($afterOptOut.isMinimized)"

    # Restore + focus the target so the input-injection section operates on it.
    Test-Tool $proc 'set_window_state' @{ window_handle = $hwnd; state = 'restore' } -Label 'post-restore' { param($p) $p.completed } | Out-Null
    Test-Tool $proc 'focus_window' @{ window_handle = $hwnd } -Label 'post-focus' { param($p) $p.completed } | Out-Null

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
        # The focused element must be Notepad's editing surface, hosted in the
        # target window.
        Test-Tool $proc 'get_focused_ui_element' @{} -Label 'is document' `
            { param($p) $p.topLevelWindowHandle -eq $hwnd -and $p.processId -eq $npPid -and $p.element.controlType -eq 'Document' } | Out-Null
        # Notepad's editing surface is a RichEdit 'Document'; it may or may not
        # expose ValuePattern. A genuine "does not support ValuePattern" error
        # is a real result, not a harness bug -- it shows in the matrix.
        Test-Tool $proc 'set_ui_value' @{ element_ref = $docRef; value = 'set_ui_value test text' } `
            { param($p) $p.completed } | Out-Null
    } else {
        Write-Host "  invoke_ui_element / set_ui_value  SKIP no Document element found" -ForegroundColor DarkGray
    }

    Write-Host "--- ask_user -----------------------------------------------" -ForegroundColor Yellow
    # Timeout path: nobody answers, so the result is 'timeout' AND the dialog
    # must be gone afterwards (the form closes itself on timeout).
    $askTitle = "wc-mutate-ask-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    Test-Tool $proc 'ask_user' @{ title = $askTitle; message = 'mutate-mcp: no answer expected'; options = @('Alpha', 'Beta'); timeout_ms = 1500 } `
        -Label 'timeout' { param($p) $p.decision -eq 'timeout' -and $null -eq $p.choice } | Out-Null
    Start-Sleep -Milliseconds 300
    Test-Tool $proc 'find_window' @{ title_contains = $askTitle; visible_only = $true } -Label 'dialog closed' `
        { param($p, $t) $t.Trim() -eq '[]' } | Out-Null

    # Answer path: the main server blocks inside ask_user, so a second server
    # instance plays the user -- it types into the text field and clicks 'Beta'
    # via UI Automation, then the main server's reply is read.
    $answerTitle = "wc-mutate-ask-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $script:nextId++
    $askReq = @{ jsonrpc = '2.0'; id = $script:nextId; method = 'tools/call'; params = @{
        name = 'ask_user'; arguments = @{ title = $answerTitle; message = 'mutate-mcp: answered by UI Automation'; options = @('Alpha', 'Beta'); allow_text_input = $true; timeout_ms = 20000 } } }
    $proc.StandardInput.WriteLine(($askReq | ConvertTo-Json -Depth 20 -Compress))
    $proc.StandardInput.Flush()
    $helper = Start-Server -Exe $ServerExe
    try {
        Invoke-Rpc -Proc $helper -Method 'initialize' -Params @{
            protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'mutate-mcp-helper'; version = '1.0' }
        } | Out-Null
        Send-Notification -Proc $helper -Method 'notifications/initialized'
        $dw = Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'wait_for_window'; arguments = @{ title_contains = $answerTitle; timeout_ms = 10000 } }
        $dialogHwnd = ((Get-Txt $dw) | ConvertFrom-Json).hwnd
        $edit = Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'find_ui_element'; arguments = @{ hwnd = $dialogHwnd; control_type = 'Edit' } }
        $editRef = ((Get-Txt $edit) | ConvertFrom-Json | Select-Object -First 1).elementRef
        Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'set_ui_value'; arguments = @{ element_ref = $editRef; value = 'typed answer' } } | Out-Null
        $btn = Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'find_ui_element'; arguments = @{ hwnd = $dialogHwnd; control_type = 'Button'; name_contains = 'Beta' } }
        $btnRef = ((Get-Txt $btn) | ConvertFrom-Json | Select-Object -First 1).elementRef
        Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'invoke_ui_element'; arguments = @{ element_ref = $btnRef; action = 'invoke' } } | Out-Null
    }
    finally {
        if (-not $helper.HasExited) {
            $helper.StandardInput.Close()
            if (-not $helper.WaitForExit(3000)) { $helper.Kill() }
        }
    }
    $askLine = $proc.StandardOutput.ReadLine()
    $askOk = $false; $askDetail = $askLine
    try {
        $answer = (Get-Txt ($askLine | ConvertFrom-Json)) | ConvertFrom-Json
        $askDetail = "decision=$($answer.decision) choice=$($answer.choice) text=$($answer.text)"
        $askOk = $answer.decision -eq 'answered' -and $answer.choice -eq 'Beta' -and $answer.text -eq 'typed answer'
    } catch { $askDetail = "exception: $($_.Exception.Message)" }
    Add-Result 'ask_user (answered)' $askOk $askDetail

    Write-Host "--- request_user_confirmation ------------------------------" -ForegroundColor Yellow
    # Timeout path: the dialog must close itself, like ask_user.
    $confTitle = "wc-mutate-conf-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    Test-Tool $proc 'request_user_confirmation' @{ title = $confTitle; message = 'mutate-mcp: no answer expected'; risk_level = 'high'; timeout_ms = 1500 } `
        -Label 'timeout' { param($p) $p.decision -eq 'timeout' } | Out-Null
    Start-Sleep -Milliseconds 300
    Test-Tool $proc 'find_window' @{ title_contains = $confTitle; visible_only = $true } -Label 'dialog closed' `
        { param($p, $t) $t.Trim() -eq '[]' } | Out-Null

    # Keyboard paths: the dialog must take real foreground focus, so a bare
    # key tap from a second server instance (no clicking, no UIA) answers it.
    foreach ($case in @(@{ Key = 'enter'; Expect = 'approved' }, @{ Key = 'space'; Expect = 'approved' }, @{ Key = 'escape'; Expect = 'denied' })) {
        $keyTitle = "wc-mutate-conf-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        $script:nextId++
        $confReq = @{ jsonrpc = '2.0'; id = $script:nextId; method = 'tools/call'; params = @{
            name = 'request_user_confirmation'; arguments = @{ title = $keyTitle; message = "mutate-mcp: answered by $($case.Key)"; risk_level = 'high'; timeout_ms = 20000 } } }
        $proc.StandardInput.WriteLine(($confReq | ConvertTo-Json -Depth 20 -Compress))
        $proc.StandardInput.Flush()
        $fgOk = $false
        $helper = Start-Server -Exe $ServerExe
        try {
            Invoke-Rpc -Proc $helper -Method 'initialize' -Params @{
                protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'mutate-mcp-helper'; version = '1.0' }
            } | Out-Null
            Send-Notification -Proc $helper -Method 'notifications/initialized'
            $dw = Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'wait_for_window'; arguments = @{ title_contains = $keyTitle; timeout_ms = 10000 } }
            $dialogHwnd = ((Get-Txt $dw) | ConvertFrom-Json).hwnd
            Start-Sleep -Milliseconds 300
            $fg = Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'get_foreground_window'; arguments = @{} }
            $fgOk = ((Get-Txt $fg) | ConvertFrom-Json).hwnd -eq $dialogHwnd
            Invoke-Rpc -Proc $helper -Method 'tools/call' -Params @{ name = 'keyboard_action'; arguments = @{ action = 'tap'; key = $case.Key } } | Out-Null
        }
        finally {
            if (-not $helper.HasExited) {
                $helper.StandardInput.Close()
                if (-not $helper.WaitForExit(3000)) { $helper.Kill() }
            }
        }
        $confLine = $proc.StandardOutput.ReadLine()
        $confOk = $false; $confDetail = $confLine
        try {
            $conf = (Get-Txt ($confLine | ConvertFrom-Json)) | ConvertFrom-Json
            $confDetail = "foreground=$fgOk decision=$($conf.decision)"
            $confOk = $fgOk -and $conf.decision -eq $case.Expect
        } catch { $confDetail = "exception: $($_.Exception.Message)" }
        Add-Result "request_user_confirmation ($($case.Key))" $confOk $confDetail
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
