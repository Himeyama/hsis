[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [string]$ExpectedTitle = 'VideoEditor セットアップ',
    [string]$UninstallDirectory,
    [switch]$ColdStart,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $SetupPath).Path
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $executable
$startInfo.WorkingDirectory = Split-Path -Parent $executable
$startInfo.UseShellExecute = $false
if ($UninstallDirectory) { $startInfo.Arguments = '--uninstall "' + $UninstallDirectory + '"' }
$extractionDirectory = $null
if ($ColdStart) {
    $extractionDirectory = Join-Path $PSScriptRoot ("../artifacts/startup-cache/" + [guid]::NewGuid().ToString('N'))
    $startInfo.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = [System.IO.Path]::GetFullPath($extractionDirectory)
}
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$testProcess = [System.Diagnostics.Process]::Start($startInfo)
try {
    while ($timer.Elapsed.TotalSeconds -lt 45) {
        $testProcess.Refresh()
        if ($testProcess.HasExited) { throw "Setup exited before opening its window: $($testProcess.ExitCode)" }
        if ($testProcess.MainWindowHandle -ne 0 -and $testProcess.MainWindowTitle -eq $ExpectedTitle) {
            $startupMilliseconds = $timer.ElapsedMilliseconds
            Start-Sleep -Seconds 1
            $testProcess.Refresh()
            if ($testProcess.HasExited) { throw 'Setup exited immediately after opening its window.' }
            $extractedBytes = 0
            if ($extractionDirectory) { $extractedBytes = (Get-ChildItem -LiteralPath $extractionDirectory -Recurse -File | Measure-Object Length -Sum).Sum }
            $report = [pscustomobject]@{ Executable=$executable; StartupMilliseconds=$startupMilliseconds; ColdStart=[bool]$ColdStart; ExtractedBytes=$extractedBytes }
            Write-Host "PASS: $ExpectedTitle ($startupMilliseconds ms, $extractedBytes extracted bytes)"
            if ($ReportPath) { $report | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding UTF8 }
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'The expected setup window did not open.'
}
finally {
    # Only terminate the process created by this startup test; closing normally opens
    # the cancellation confirmation dialog. This test never starts installation.
    if (!$testProcess.HasExited) { $testProcess.Kill(); $testProcess.WaitForExit() }
    $testProcess.Dispose()
}
