[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location $PSScriptRoot
try {
    [xml]$props = Get-Content -LiteralPath ./Directory.Build.props
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Directory.Build.props must contain a MAJOR.MINOR.PATCH version.'
    }
    # Isolate each build so previous publish output cannot enter the installer.
    $work = Join-Path $PSScriptRoot "artifacts/self-build/$([guid]::NewGuid().ToString('N'))"
    $payload = Join-Path $work 'payload'
    $cli = Join-Path $work 'cli'

    dotnet publish ./src/Hsis.App/Hsis.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'HSIS Studio publish failed.' }
    dotnet publish ./src/Hsis.Cli/Hsis.Cli.csproj -c Release -r win-x64 --self-contained true -o $cli
    if ($LASTEXITCODE -ne 0) { throw 'HSIS CLI publish failed.' }

    # Studio and CLI share the same current-source Template and .NET runtime.
    Copy-Item -Path "$cli/*" -Destination $payload -Recurse -Force
    foreach ($required in @('Hsis.App.exe', 'hsis.exe', 'Template/src/Hsis.App/Hsis.ico', 'Template/src/Hsis.Bootstrap/Bootstrap.cpp')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
            throw "Missing installer payload: $required"
        }
    }

    & (Join-Path $cli 'hsis.exe') build ./installer/HSIS.nsi --output (Join-Path $work 'installer') "-DAPP_VERSION=$version" "-DSOURCE_DIR=$payload"
    if ($LASTEXITCODE -ne 0) { throw 'HSIS installer generation failed.' }
    $installers = @(Get-ChildItem -Path "$work/installer/Setup-*/HSIS-$version-win-x64-Setup.exe" -File)
    if ($installers.Count -ne 1) { throw 'Expected exactly one HSIS installer.' }

    $distribution = Join-Path $PSScriptRoot 'artifacts/distribution'
    New-Item -ItemType Directory -Path $distribution -Force | Out-Null
    $installer = $installers[0]
    # Preserve the generated filename, which is used by the native launcher.
    Copy-Item -LiteralPath $installer.FullName -Destination $distribution -Force
    $hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($installer.Name)" | Set-Content -LiteralPath (Join-Path $distribution "$($installer.Name).sha256") -Encoding ascii
    Write-Host "Installer: $(Join-Path $distribution $installer.Name)"
}
finally {
    Pop-Location
}
