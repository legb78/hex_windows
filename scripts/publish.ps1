<#
.SYNOPSIS
    Builds the self-contained HexWin executable.

.DESCRIPTION
    Produces a single self-contained file: neither .NET nor any dependency has
    to be installed on the target machine. The sherpa-onnx native libraries are
    embedded in the executable and extracted on first run.

    The executable weighs about 120 MB — that is the price of bundling the .NET
    runtime and the recognition engine. It compresses to roughly 50 MB in the
    published archive. The model itself is downloaded separately with
    get-model.ps1.

.PARAMETER Output
    Destination folder. Defaults to publish/ at the repository root.

.EXAMPLE
    .\scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Output) {
    $Output = Join-Path $repoRoot 'publish'
}

Write-Host "Destination : $Output"
Write-Host ''

& dotnet publish (Join-Path $repoRoot 'src\HexWin\HexWin.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed (exit code $LASTEXITCODE)."
}

$exe = Join-Path $Output 'HexWin.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB)

Write-Host ''
Write-Host "Executable built: $exe ($size MB)" -ForegroundColor Green
Write-Host ''
Write-Host "The model is not included. On the target machine:"
Write-Host "  .\get-model.ps1"
