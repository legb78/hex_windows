<#
.SYNOPSIS
    Télécharge le modèle de reconnaissance vocale dans le dossier models/.

.DESCRIPTION
    Les modèles ne sont pas versionnés dans le dépôt : ils pèsent plusieurs
    centaines de mégaoctets, très au-delà de la limite de 100 Mo de GitHub.

    Le modèle par défaut est Parakeet TDT 0.6B v3 de NVIDIA, celui qu'utilise
    Hex sur macOS. Contrairement à Whisper, il se présente en plusieurs
    fichiers — encodeur, décodeur, joiner et vocabulaire — d'où une archive à
    extraire plutôt qu'un fichier unique.

    Le téléchargement passe par un fichier temporaire, et l'extraction n'a lieu
    qu'une fois l'archive complète. Une coupure réseau ne laisse donc jamais un
    modèle partiellement écrit, que le moteur chargerait avant d'échouer de
    façon incompréhensible.

.PARAMETER Model
    parakeet-v3  (~578 Mo extrait) 25 langues européennes dont le français.
                                   Le défaut, et le plus rapide.
    parakeet-v2  (~578 Mo extrait) anglais uniquement, légèrement plus précis
                                   sur cette langue.

.PARAMETER Force
    Retélécharge même si le modèle est déjà présent.

.EXAMPLE
    .\scripts\get-model.ps1
    .\scripts\get-model.ps1 -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('parakeet-v3', 'parakeet-v2')]
    [string]$Model = 'parakeet-v3',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 négocie encore TLS 1.0 par défaut, que GitHub refuse.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$archives = @{
    'parakeet-v3' = 'sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8'
    'parakeet-v2' = 'sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8'
}

$modelName = $archives[$Model]
$archiveName = "$modelName.tar.bz2"
$url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$archiveName"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $repoRoot 'models'
$destination = Join-Path $modelsDir $modelName
$archivePath = Join-Path $modelsDir $archiveName

# Fichiers que le moteur exige : leur présence sert de test de complétude.
$requiredFiles = @('encoder.int8.onnx', 'decoder.int8.onnx', 'joiner.int8.onnx', 'tokens.txt')

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir | Out-Null
}

Write-Host "Modèle      : $modelName"
Write-Host "Destination : $destination"

if ((Test-Path $destination) -and -not $Force) {
    $missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $destination $_)) }

    if ($missing.Count -eq 0) {
        Write-Host "Déjà présent et complet, rien à faire." -ForegroundColor Green
        Write-Host "Utilisez -Force pour retélécharger."
        return
    }

    Write-Warning "Modèle incomplet (manque : $($missing -join ', ')), retéléchargement."
}

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromHours(2)

try {
    Write-Host ''
    $response = $client.GetAsync($url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    $response.EnsureSuccessStatusCode() | Out-Null

    $expectedBytes = $response.Content.Headers.ContentLength
    if ($expectedBytes) {
        Write-Host ("Archive     : {0:N0} Mo à télécharger" -f [math]::Round($expectedBytes / 1MB))
    }

    $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $target = [System.IO.File]::Create($archivePath)

    try {
        $buffer = New-Object byte[] (1MB)
        $downloaded = 0L
        $lastReport = [DateTime]::MinValue

        while ($true) {
            $read = $source.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) { break }

            $target.Write($buffer, 0, $read)
            $downloaded += $read

            # Rafraîchir la barre à chaque bloc coûterait plus cher que le
            # téléchargement lui-même.
            if (([DateTime]::UtcNow - $lastReport).TotalMilliseconds -ge 500) {
                $lastReport = [DateTime]::UtcNow
                if ($expectedBytes) {
                    $percent = [math]::Min([math]::Round(($downloaded / $expectedBytes) * 100, 1), 100)
                    Write-Progress -Activity "Téléchargement de $archiveName" `
                        -Status ("{0:N0} Mo sur {1:N0} Mo" -f ($downloaded / 1MB), ($expectedBytes / 1MB)) `
                        -PercentComplete $percent
                }
            }
        }
    }
    finally {
        $target.Dispose()
        $source.Dispose()
        Write-Progress -Activity "Téléchargement de $archiveName" -Completed
    }

    $actual = (Get-Item $archivePath).Length
    if ($expectedBytes -and ($actual -ne $expectedBytes)) {
        throw ("Téléchargement incomplet : {0:N0} octets reçus sur {1:N0} attendus." -f $actual, $expectedBytes)
    }

    Write-Host "Extraction..."

    # tar est livré avec Windows 10 et 11, et gère le bzip2.
    & tar -xjf $archivePath -C $modelsDir
    if ($LASTEXITCODE -ne 0) {
        throw "L'extraction a échoué (code $LASTEXITCODE)."
    }

    $missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $destination $_)) }
    if ($missing.Count -gt 0) {
        throw "Archive extraite mais incomplète, il manque : $($missing -join ', ')"
    }

    Write-Host ''
    Write-Host "Modèle installé." -ForegroundColor Green
    Write-Host ''
    Write-Host "Vérifiez la chaîne de transcription avec :"
    Write-Host "  dotnet build -c Release"
    Write-Host "  .\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe --transcribe mon-fichier.wav"
}
finally {
    $client.Dispose()
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    }
}
