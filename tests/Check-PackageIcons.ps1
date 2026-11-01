[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HsisPath,
    [Parameter(Mandatory)][string]$IconlessExecutable
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../'))
$work = Join-Path $root ('artifacts/package-icons/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
Copy-Item -LiteralPath $IconlessExecutable -Destination (Join-Path $work 'Main.exe')
$bitmap = [Drawing.Bitmap]::new(256, 256)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$png = [IO.MemoryStream]::new()
try {
    $graphics.Clear([Drawing.Color]::Lime)
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $work 'Main.ico')))
    try {
        $writer.Write([ushort]0); $writer.Write([ushort]1); $writer.Write([ushort]1)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([ushort]0)
        $writer.Write([ushort]1); $writer.Write([ushort]32)
        $writer.Write([int]$png.Length); $writer.Write([int]22); $writer.Write($png.ToArray())
    } finally { $writer.Dispose() }
} finally { $png.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

foreach ($custom in @($true, $false)) {
    $name = if ($custom) { 'Custom' } else { 'Fallback' }
    $files = if ($custom) { 'File "Main.exe"' + "`n" + 'File "Main.ico"' } else { 'File "Main.exe"' }
    # Keep the same iconless EXE in both cases; only the custom case supplies an icon.
    if (!$custom) { Rename-Item -LiteralPath (Join-Path $work 'Main.ico') -NewName 'Unused.ico' }
    $script = Join-Path $work "$name.nsi"
    @"
Name "Main"
OutFile "$name.exe"
InstallDir "`$LOCALAPPDATA\Programs\HSIS Icon Test"
Section
SetOutPath "`$INSTDIR"
$files
SectionEnd
"@ | Set-Content -LiteralPath $script -Encoding utf8
    $output = Join-Path $work $name
    & $HsisPath build $script --output $output
    if ($LASTEXITCODE -ne 0) { throw "$name build failed" }
    $setups = @(Get-ChildItem -LiteralPath $output -Recurse -Filter "$name.exe" -File)
    if ($setups.Count -ne 1) { throw "$name setup not found" }
    $setup = $setups[0]
    $inputFile = [IO.File]::OpenRead($setup.FullName)
    try {
        $inputFile.Position = $inputFile.Length - 16
        $reader = [IO.BinaryReader]::new($inputFile)
        $offset = $reader.ReadInt64(); $length = $reader.ReadInt64()
        $inputFile.Position = $offset
        $uninstaller = Join-Path $work "$name-Uninstall.exe"
        [IO.File]::WriteAllBytes($uninstaller, $reader.ReadBytes([int]$length))
    } finally { $inputFile.Dispose() }
    foreach ($executable in @($setup.FullName, $uninstaller)) {
        $actualIcon = [Drawing.Icon]::ExtractAssociatedIcon($executable)
        $actual = $actualIcon.ToBitmap()
        try {
            if ($custom) {
                $pixel = $actual.GetPixel([int]($actual.Width / 2), [int]($actual.Height / 2))
                if ($pixel.G -ne 255 -or $pixel.R -ne 0 -or $pixel.B -ne 0) { throw "Custom icon missing: $executable" }
            } else {
                $expectedIcon = [Drawing.Icon]::new((Join-Path $root 'src/Hsis.App/Hsis.ico'), $actual.Width, $actual.Height)
                $expected = $expectedIcon.ToBitmap()
                try {
                    for ($y = 0; $y -lt $actual.Height; $y++) {
                        for ($x = 0; $x -lt $actual.Width; $x++) {
                            if ($actual.GetPixel($x, $y).ToArgb() -ne $expected.GetPixel($x, $y).ToArgb()) {
                                throw "HSIS fallback icon mismatch: $executable"
                            }
                        }
                    }
                } finally { $expected.Dispose(); $expectedIcon.Dispose() }
            }
        } finally { $actual.Dispose(); $actualIcon.Dispose() }
        Write-Host "PASS: $name icon in $executable"
    }
}
