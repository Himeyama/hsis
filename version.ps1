[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('patch', 'minor', 'major')][string]$Part,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Message,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Summary
)

$ErrorActionPreference = 'Stop'
function Invoke-Git {
    param([string[]]$GitArguments)
    $result = & git -C $PSScriptRoot @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArguments -join ' ') failed ($LASTEXITCODE)." }
    return $result
}

$propsPath = Join-Path $PSScriptRoot 'Directory.Build.props'
$logPath = Join-Path $PSScriptRoot 'CHANGELOG.md'
$props = [IO.File]::ReadAllText($propsPath)
$log = [IO.File]::ReadAllText($logPath)
$match = [regex]::Match($props, '<Version>(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $match.Success) { throw 'Directory.Build.props must contain a numeric Version.' }
$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value
switch ($Part) {
    major { $major++; $minor = 0; $patch = 0 }
    minor { $minor++; $patch = 0 }
    patch { $patch++ }
}
$version = "$major.$minor.$patch"
$tag = "v$version"
if (Invoke-Git @('ls-files', '-u')) { throw 'Resolve merge conflicts first.' }
if (-not (Invoke-Git @('diff', '--cached', '--name-only'))) { throw 'Stage the changes to commit first.' }
foreach ($path in @('Directory.Build.props', 'CHANGELOG.md')) {
    if (Invoke-Git @('diff', '--name-only', '--', $path)) {
        throw "Stage or discard unstaged changes in $path first."
    }
}
if (Invoke-Git @('tag', '--list', $tag)) { throw "Tag $tag already exists; existing tags are never replaced." }
if ($log -match "(?m)^## v$([regex]::Escape($version))\b") { throw "CHANGELOG already contains $version." }
if (-not $PSCmdlet.ShouldProcess($PSScriptRoot, "Update to $version, commit staged changes and create $tag")) { return }

$newline = if ($log.Contains("`r`n")) { "`r`n" } else { "`n" }
$headingEnd = $log.IndexOf("`n")
if ($headingEnd -lt 0) { throw 'CHANGELOG must have a heading followed by a newline.' }
$entry = "## $tag — $(Get-Date -Format 'yyyy-MM-dd')$newline$newline- $($Summary.Trim())$newline$newline"
$newLog = $log.Substring(0, $headingEnd + 1) + $newline + $entry + $log.Substring($headingEnd + 1).TrimStart([char[]]"`r`n")
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($propsPath, $props.Replace($match.Value, "<Version>$version</Version>"), $encoding)
[IO.File]::WriteAllText($logPath, $newLog, $encoding)
Invoke-Git @('add', '--', 'Directory.Build.props', 'CHANGELOG.md')
Invoke-Git @('commit', '-m', $Message)
try {
    Invoke-Git @('tag', $tag, 'HEAD')
} catch {
    throw "Commit succeeded but tagging failed. After checking HEAD, run: git tag $tag HEAD. $($_.Exception.Message)"
}
Write-Host "Created $tag. No push was performed."
