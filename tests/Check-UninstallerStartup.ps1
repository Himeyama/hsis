[CmdletBinding()]
param([Parameter(Mandatory)][string]$UninstallerPath, [string]$ExpectedTitle = 'Hello App アンインストール')

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $UninstallerPath).Path
$waiting = Start-Process -FilePath $executable -ArgumentList @('--uninstall', ('"' + (Split-Path -Parent $executable) + '"'), '--wait-pid', $PID) -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 2
    $waiting.Refresh()
    if ($waiting.HasExited) { throw "Uninstaller exited before parent wait: $($waiting.ExitCode)" }
    if ($waiting.Modules.ModuleName -contains 'Microsoft.WindowsAppRuntime.dll') {
        throw 'Windows App Runtime loaded before the uninstaller handoff completed.'
    }
    Write-Host 'PASS: parent wait does not load Windows App Runtime'
}
finally {
    if (!$waiting.HasExited) { $waiting.Kill(); $waiting.WaitForExit() }
    $waiting.Dispose()
}
$launcher = Start-Process -FilePath $executable -PassThru -WindowStyle Hidden
$child = $null
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    while ($timer.Elapsed.TotalSeconds -lt 45) {
        if ($null -eq $child) {
            $candidate = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($launcher.Id)" |
                Where-Object { $_.Name -eq [IO.Path]::GetFileName($executable) } | Select-Object -First 1
            if ($candidate) { $child = Get-Process -Id $candidate.ProcessId }
        }
        if ($child) {
            $child.Refresh()
            if ($child.HasExited) { throw "Uninstaller child exited: $($child.ExitCode)" }
            if ($child.MainWindowTitle -eq $ExpectedTitle -and $child.MainWindowHandle -ne 0) {
                Start-Sleep -Seconds 1
                $child.Refresh()
                if ($child.HasExited) { throw 'Uninstaller closed after opening.' }
                Write-Host "PASS: normal uninstaller handoff opened $ExpectedTitle"
                return
            }
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'Uninstaller handoff did not open its window.'
}
finally {
    # This test only opens the confirmation screen; it never starts removal.
    foreach ($testProcess in @($child, $launcher)) {
        if ($testProcess) {
            if (!$testProcess.HasExited) { $testProcess.Kill(); $testProcess.WaitForExit() }
            $testProcess.Dispose()
        }
    }
}
