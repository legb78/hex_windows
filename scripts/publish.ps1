<#
.SYNOPSIS
    Produit l'exécutable autonome de HexWin.

.DESCRIPTION
    Compile en un fichier unique, autonome : ni .NET ni aucune dépendance à
    installer sur la machine cible. Les bibliothèques natives de sherpa-onnx
    sont embarquées dans l'exécutable et extraites au premier lancement.

    L'exécutable pèse environ 126 Mo — c'est le prix du runtime .NET et du
    moteur de reconnaissance embarqués. Le modèle, lui, reste à télécharger
    séparément avec get-model.ps1.

.PARAMETER Output
    Dossier de destination. Par défaut publish/ à la racine du dépôt.

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
    throw "La publication a échoué (code $LASTEXITCODE)."
}

$exe = Join-Path $Output 'HexWin.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB)

Write-Host ''
Write-Host "Exécutable produit : $exe ($size Mo)" -ForegroundColor Green
Write-Host ''
Write-Host "Le modèle n'est pas inclus. Sur la machine cible :"
Write-Host "  .\get-model.ps1"
