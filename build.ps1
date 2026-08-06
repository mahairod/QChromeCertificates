param(
    [string]$OutputPath,
    [switch]$SelfTestBuild
)

$ErrorActionPreference = "Stop"

$projectDirectory = $PSScriptRoot
$versionPath = Join-Path $projectDirectory "VERSION"
$version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION must contain three numeric components, for example 1.0.0."
}

$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

$assemblyVersion = "$version.0"
$output = if ([String]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $projectDirectory "ChromeCertificatePolicyManager.exe"
} else {
    [IO.Path]::GetFullPath($OutputPath)
}
$generatedSource = Join-Path ([IO.Path]::GetTempPath()) ("ChromeCertificatePolicyManager.Version.{0}.cs" -f [Guid]::NewGuid().ToString("N"))
$generatedManifest = Join-Path ([IO.Path]::GetTempPath()) ("ChromeCertificatePolicyManager.Manifest.{0}.manifest" -f [Guid]::NewGuid().ToString("N"))
$utf8 = New-Object System.Text.UTF8Encoding($false)
$source = @"
using System.Reflection;

[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$version")]
[assembly: AssemblyTitle("Chrome Certificate Policy Manager")]
[assembly: AssemblyProduct("Chrome Certificate Policy Manager")]
[assembly: AssemblyDescription("Manage constrained certificate authority policies for Chromium-based browsers")]
"@

try {
    [IO.File]::WriteAllText($generatedSource, $source, $utf8)
    $manifestTemplatePath = Join-Path $projectDirectory "app.manifest"
    $manifest = [IO.File]::ReadAllText($manifestTemplatePath, [Text.Encoding]::UTF8)
    $manifestVersionToken = 'version="0.0.0.0"'
    if (-not $manifest.Contains($manifestVersionToken)) {
        throw "app.manifest must contain $manifestVersionToken."
    }
    $manifest = $manifest.Replace($manifestVersionToken, "version=`"$assemblyVersion`"")
    [IO.File]::WriteAllText($generatedManifest, $manifest, $utf8)

    $target = if ($SelfTestBuild) { "/target:exe" } else { "/target:winexe" }
    $arguments = @(
        "/nologo",
        $target,
        "/optimize+",
        "/resource:$versionPath,ChromeCertificatePolicyManager.VERSION",
        "/out:$output",
        "/reference:System.dll",
        "/reference:System.Core.dll",
        "/reference:System.Drawing.dll",
        "/reference:System.Windows.Forms.dll",
        "/reference:System.Web.Extensions.dll",
        "$projectDirectory\ChromeCertificatePolicyManager.cs",
        $generatedSource
    )

    if (-not $SelfTestBuild) {
        $arguments = @(
            "/win32manifest:$generatedManifest",
            "/win32icon:$projectDirectory\app.ico"
        ) + $arguments
    }

    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Remove-Item -LiteralPath $generatedSource -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $generatedManifest -Force -ErrorAction SilentlyContinue
}
