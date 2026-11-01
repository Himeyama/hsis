[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$ExpectedTitle,
    [switch]$RequirePreparing,
    [switch]$CancelDuringPreparing,
    [switch]$RequireSpinner,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $SetupPath).Path
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.WorkingDirectory = Split-Path -Parent $executable
$watch = [Diagnostics.Stopwatch]::StartNew()
$launcher = [Diagnostics.Process]::Start($start)
$tracked = @{}
$tracked[$launcher.Id] = $launcher
$preparingMs = $null
$managed = $null
$runtimeRoot = $null
$cancelSent = $false
$spinnerFrames = [Collections.Generic.HashSet[string]]::new()
$fontFamily = $null
if ($RequireSpinner -and !('HsisPreparingControls' -as [type])) {
    Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class HsisPreparingControls {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string name, IntPtr title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr window, string name);
}
'@
}
if ($CancelDuringPreparing) {
    Add-Type 'using System; using System.Runtime.InteropServices; public static class HsisStartupCancel { [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l); }'
}
try {
    while ($watch.Elapsed.TotalSeconds -lt 90) {
        foreach ($candidate in Get-CimInstance Win32_Process -Filter "Name='Hsis.Bootstrap.exe' OR Name='Hsis.Setup.exe'") {
            if ($candidate.CommandLine -and $candidate.CommandLine.Contains($executable, [StringComparison]::OrdinalIgnoreCase)) {
                if (!$tracked.ContainsKey([int]$candidate.ProcessId)) {
                    try { $tracked[[int]$candidate.ProcessId] = Get-Process -Id $candidate.ProcessId -ErrorAction Stop } catch { continue }
                }
            }
        }
        foreach ($process in @($tracked.Values)) {
            $process.Refresh()
            if ($process.HasExited) { continue }
            if ($process.MainWindowTitle.EndsWith('準備しています') -and $process.MainWindowHandle -ne 0) {
                if ($null -eq $preparingMs) { $preparingMs = $watch.ElapsedMilliseconds }
                if ($process.Modules.ModuleName -contains 'Microsoft.WindowsAppRuntime.dll' -or $process.Modules.ModuleName -contains 'coreclr.dll') {
                    throw 'The preparing window must not depend on .NET or WinUI.'
                }
                if ($RequireSpinner -and $process.ProcessName -eq 'Hsis.Bootstrap') {
                    if ([HsisPreparingControls]::FindWindowExW($process.MainWindowHandle, [IntPtr]::Zero, 'msctls_progress32', [IntPtr]::Zero) -ne [IntPtr]::Zero) {
                        throw 'The progress bar must be replaced by the braille spinner.'
                    }
                    $control = [IntPtr]::Zero
                    while (($control = [HsisPreparingControls]::FindWindowExW($process.MainWindowHandle, $control, 'STATIC', [IntPtr]::Zero)) -ne [IntPtr]::Zero) {
                        $text = [Text.StringBuilder]::new(512)
                        $null = [HsisPreparingControls]::GetWindowTextW($control, $text, $text.Capacity)
                        if ($text.ToString() -match '^[\u2800-\u28ff]$') { $null = $spinnerFrames.Add($text.ToString()) }
                    }
                    if ([HsisPreparingControls]::GetPropW($process.MainWindowHandle, 'Hsis.NotoSansJP') -eq [IntPtr]::new(1)) { $fontFamily = 'Noto Sans JP' }
                }
                if ($CancelDuringPreparing -and !$cancelSent -and $process.ProcessName -eq 'Hsis.Bootstrap') {
                    $runtimeRoot = Join-Path (Split-Path -Parent $process.MainModule.FileName) 'runtime'
                    $null = [HsisStartupCancel]::PostMessageW($process.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
                    $cancelSent = $true
                }
            }
            if ($process.MainWindowTitle -eq $ExpectedTitle -and $process.MainWindowHandle -ne 0) {
                $managed = $process
                $runtimeRoot = Split-Path -Parent $process.MainModule.FileName
                break
            }
        }
        if ($managed) { break }
        if ($cancelSent -and @($tracked.Values | Where-Object { !$_.HasExited }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 40
    }
    if ($CancelDuringPreparing) {
        if (!$cancelSent -or $managed) { throw 'Preparing cancellation did not stop startup.' }
        foreach ($process in $tracked.Values) { if (!$process.HasExited) { throw 'Cancelled process is still running.' } }
        Write-Host 'PASS: preparing cancellation stopped extraction without opening the installer'
    } else {
    if (!$managed) { throw 'The setup window did not open.' }
    if ($RequirePreparing -and $null -eq $preparingMs) { throw 'The preparing window was not observed.' }
    $startupMs = $watch.ElapsedMilliseconds
    Start-Sleep -Seconds 1
    $managed.Refresh()
    if ($managed.HasExited) { throw 'The setup window closed immediately.' }
    if ($RequireSpinner -and ($spinnerFrames.Count -lt 2 -or !$fontFamily -or !$fontFamily.StartsWith('Noto Sans JP'))) {
        throw "Spinner or font check failed: frames=$($spinnerFrames.Count), font=$fontFamily"
    }
    $report = [pscustomobject]@{
        Executable = $executable
        PreparingObserved = $null -ne $preparingMs
        PreparingObservedMilliseconds = $preparingMs
        WindowObservedMilliseconds = $startupMs
        RuntimeBytes = (Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse | Measure-Object Length -Sum).Sum
        RuntimeDirectory = $runtimeRoot
        SpinnerFramesObserved = @($spinnerFrames)
        PreparingFont = $fontFamily
    }
    if ($ReportPath) { $report | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding utf8 }
    Write-Host "PASS: preparing=$preparingMs ms, window=$startupMs ms, title=$ExpectedTitle"
    if ($RequireSpinner) { Write-Host "PASS: animated braille frames=$($spinnerFrames.Count), font=$fontFamily, no progress bar" }
    }
} finally {
    # Only terminate processes started by this test. Installation/removal is never started.
    if ($managed) { $managed.Refresh(); if (!$managed.HasExited) { $managed.Kill(); $managed.WaitForExit() } }
    foreach ($process in @($tracked.Values)) {
        $process.Refresh()
        if (!$process.HasExited -and !$process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
if ($runtimeRoot -and (Test-Path -LiteralPath $runtimeRoot)) { throw 'Extracted runtime was not cleaned after exit.' }
Write-Host 'PASS: large temporary runtime files were cleaned after exit'
