[CmdletBinding()]
param(
    [ValidateSet('studio', 'sample')]
    [string]$Target = 'studio',
    [switch]$Build,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

try {
    if ($Target -eq 'sample' -and $Build) {
        throw 'The -Build option is available for studio only.'
    }

    if ($Target -eq 'studio') {
        $projectPath = Join-Path $PSScriptRoot 'src\Hsis.App\Hsis.App.csproj'
        $outputDirectory = Join-Path $PSScriptRoot 'artifacts\studio'
        $executablePath = Join-Path $outputDirectory 'Hsis.App.exe'

        # Always publish the current source before launching Studio.
        & {
            & dotnet publish $projectPath -c Release -p:Platform=x64 -o $outputDirectory
            if ($LASTEXITCODE -ne 0) {
                throw "Build failed (exit code $LASTEXITCODE)."
            }
        }
    }
    else {
        $sampleDirectory = Join-Path $PSScriptRoot 'artifacts\checks'
        if (-not (Test-Path -LiteralPath $sampleDirectory -PathType Container)) {
            throw 'No generated sample found in artifacts\checks. Create a sample first.'
        }
        $latestSample = Get-ChildItem -LiteralPath $sampleDirectory -Directory -Filter 'Setup-*' |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Setup.exe') -PathType Leaf } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($null -eq $latestSample) {
            throw 'No generated Setup.exe found in artifacts\checks.'
        }
        $executablePath = Join-Path $latestSample.FullName 'Setup.exe'
    }

    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Executable not found: $executablePath"
    }
    Write-Host $executablePath
    if (-not $NoLaunch) {
        Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path -Parent $executablePath)
    }
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
