<#
.SYNOPSIS
    Télécharge un modèle Whisper au format GGML dans le dossier models/.

.DESCRIPTION
    Les modèles ne sont pas versionnés dans le dépôt : le plus gros pèse
    1,5 Go, très au-delà de la limite de 100 Mo de GitHub.

    Le téléchargement se fait dans un fichier temporaire renommé seulement une
    fois complet. Une coupure réseau ne laisse donc jamais un modèle
    partiellement écrit que Whisper accepterait de charger avant d'échouer de
    façon incompréhensible.

.PARAMETER Model
    large-v3-turbo      (~1,5 Go) le plus précis en français, et rapide. Défaut.
    large-v3-turbo-q5_0 (~535 Mo) même modèle quantifié : 3x plus léger,
                                  un peu moins précis, sensiblement plus rapide.
    medium              (~1,4 Go) plus lent que turbo pour une qualité voisine.
    small               (~455 Mo) rapide, fautes plus fréquentes sur les noms propres.
    tiny                (~73 Mo)  pour les tests d'intégration uniquement.

.PARAMETER Force
    Retélécharge même si le fichier est déjà présent et complet.

.EXAMPLE
    .\scripts\get-model.ps1
    .\scripts\get-model.ps1 -Model tiny
#>
[CmdletBinding()]
param(
    [ValidateSet('large-v3-turbo', 'large-v3-turbo-q5_0', 'medium', 'small', 'tiny')]
    [string]$Model = 'large-v3-turbo',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 négocie encore TLS 1.0 par défaut, que Hugging Face refuse.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$fileName = "ggml-$Model.bin"
$url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$fileName"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $repoRoot 'models'
$destination = Join-Path $modelsDir $fileName
$partial = "$destination.part"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir | Out-Null
}

Write-Host "Modèle      : $Model"
Write-Host "Destination : $destination"

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromHours(2)

try {
    # Taille attendue lue sur le serveur plutôt que codée en dur : elle reste
    # juste même si le modèle est republié.
    $head = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Head, $url)
    $headResponse = $client.SendAsync($head).GetAwaiter().GetResult()
    $headResponse.EnsureSuccessStatusCode() | Out-Null
    $expectedBytes = $headResponse.Content.Headers.ContentLength

    if ($expectedBytes) {
        Write-Host ("Taille      : {0:N0} Mo" -f [math]::Round($expectedBytes / 1MB))
    }

    if ((Test-Path $destination) -and -not $Force) {
        $actual = (Get-Item $destination).Length
        if ((-not $expectedBytes) -or ($actual -eq $expectedBytes)) {
            Write-Host "Déjà présent et complet, rien à faire." -ForegroundColor Green
            Write-Host "Utilisez -Force pour retélécharger."
            return
        }
        Write-Warning ("Fichier incomplet ({0:N0} octets au lieu de {1:N0}), reprise du téléchargement." -f $actual, $expectedBytes)
    }

    Write-Host ''
    $response = $client.GetAsync($url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    $response.EnsureSuccessStatusCode() | Out-Null

    $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $target = [System.IO.File]::Create($partial)

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
                    $percent = [math]::Round(($downloaded / $expectedBytes) * 100, 1)
                    Write-Progress -Activity "Téléchargement de $fileName" `
                        -Status ("{0:N0} Mo sur {1:N0} Mo" -f ($downloaded / 1MB), ($expectedBytes / 1MB)) `
                        -PercentComplete ([math]::Min($percent, 100))
                }
                else {
                    Write-Progress -Activity "Téléchargement de $fileName" `
                        -Status ("{0:N0} Mo" -f ($downloaded / 1MB))
                }
            }
        }
    }
    finally {
        $target.Dispose()
        $source.Dispose()
        Write-Progress -Activity "Téléchargement de $fileName" -Completed
    }

    $actual = (Get-Item $partial).Length
    if ($expectedBytes -and ($actual -ne $expectedBytes)) {
        Remove-Item $partial -Force
        throw ("Téléchargement incomplet : {0:N0} octets reçus sur {1:N0} attendus." -f $actual, $expectedBytes)
    }

    Move-Item -Path $partial -Destination $destination -Force

    Write-Host ''
    Write-Host "Modèle téléchargé." -ForegroundColor Green
    Write-Host ''
    Write-Host "Vérifiez la chaîne de transcription avec :"
    Write-Host "  dotnet build -c Release"
    Write-Host "  .\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe --transcribe mon-fichier.wav"
}
finally {
    $client.Dispose()
    if (Test-Path $partial) {
        Remove-Item $partial -Force -ErrorAction SilentlyContinue
    }
}
