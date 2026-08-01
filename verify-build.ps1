param(
    [switch]$RunSelfTest
)

$ErrorActionPreference = "Stop"
$projectDirectory = $PSScriptRoot
$executable = Join-Path $projectDirectory "ChromeCertificatePolicyManager.exe"
$version = (Get-Content -LiteralPath (Join-Path $projectDirectory "VERSION") -Raw).Trim()
$expectedFileVersion = "$version.0"

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Executable not found: $executable"
}

if ($RunSelfTest) {
    $selfTestExecutable = Join-Path ([IO.Path]::GetTempPath()) ("ChromeCertificatePolicyManager.SelfTest.{0}.exe" -f [Guid]::NewGuid().ToString("N"))
    try {
        & (Join-Path $projectDirectory "build.ps1") -OutputPath $selfTestExecutable -SelfTestBuild
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build self-test executable."
        }

        & $selfTestExecutable --self-test --registry-path "Software\ChromeCertificatePolicyManager\CI"
        if ($LASTEXITCODE -ne 0) {
            throw "Self-test failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $selfTestExecutable -Force -ErrorAction SilentlyContinue
    }
}

$fileInfo = (Get-Item -LiteralPath $executable).VersionInfo
if ($fileInfo.FileVersion -ne $expectedFileVersion) {
    throw "FileVersion is '$($fileInfo.FileVersion)', expected '$expectedFileVersion'."
}
if (-not $fileInfo.ProductVersion.StartsWith($version, [StringComparison]::Ordinal)) {
    throw "ProductVersion is '$($fileInfo.ProductVersion)', expected '$version'."
}

$assembly = [Reflection.Assembly]::LoadFile($executable)
$resourceName = "ChromeCertificatePolicyManager.VERSION"
$stream = $assembly.GetManifestResourceStream($resourceName)
if ($null -eq $stream) {
    throw "Embedded VERSION resource not found."
}
$reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8, $true)
try {
    $embeddedVersion = $reader.ReadToEnd().Trim()
}
finally {
    $reader.Dispose()
}
if ($embeddedVersion -ne $version) {
    throw "Embedded version is '$embeddedVersion', expected '$version'."
}

if (-not (Select-String -Path $executable -Pattern "requireAdministrator" -SimpleMatch -Quiet)) {
    throw "The executable does not contain the requireAdministrator manifest."
}

Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::ExtractAssociatedIcon($executable)
if ($null -eq $icon) {
    throw "The executable does not contain an application icon."
}
$expectedIcon = New-Object Drawing.Icon -ArgumentList @((Join-Path $projectDirectory "app.ico"), 32, 32)
$actualBitmap = $icon.ToBitmap()
$expectedBitmap = $expectedIcon.ToBitmap()
try {
    if ($actualBitmap.Size -ne $expectedBitmap.Size) {
        throw "The embedded application icon has an unexpected size."
    }
    for ($y = 0; $y -lt $actualBitmap.Height; $y++) {
        for ($x = 0; $x -lt $actualBitmap.Width; $x++) {
            if ($actualBitmap.GetPixel($x, $y).ToArgb() -ne $expectedBitmap.GetPixel($x, $y).ToArgb()) {
                throw "The embedded application icon does not match app.ico."
            }
        }
    }
}
finally {
    $actualBitmap.Dispose()
    $expectedBitmap.Dispose()
    $icon.Dispose()
    $expectedIcon.Dispose()
}

Write-Output "Verified ChromeCertificatePolicyManager $version"
