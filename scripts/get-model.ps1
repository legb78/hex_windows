<#
.SYNOPSIS
    Downloads the speech recognition model into the models/ folder.

.DESCRIPTION
    Models are not tracked in the repository: they weigh several hundred
    megabytes, far past GitHub's 100 MB limit.

    The default is NVIDIA's Parakeet TDT 0.6B v3, the same engine Hex uses on
    macOS. Unlike Whisper it comes as several files — encoder, decoder, joiner
    and vocabulary — hence an archive to extract rather than a single file.

    The download goes to a temporary file and is only extracted once complete.
    A dropped connection therefore never leaves a partially written model that
    the engine would load before failing in some incomprehensible way.

.PARAMETER Model
    parakeet-v3  (~578 MB extracted) 25 European languages including French.
                                     The default, and the fastest.
    parakeet-v2  (~578 MB extracted) English only, marginally more accurate on
                                     that language.

.PARAMETER Force
    Downloads again even if the model is already present.

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

# PowerShell 5.1 still negotiates TLS 1.0 by default, which GitHub refuses.
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

# Files the engine requires. Their presence doubles as a completeness check.
$requiredFiles = @('encoder.int8.onnx', 'decoder.int8.onnx', 'joiner.int8.onnx', 'tokens.txt')

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir | Out-Null
}

Write-Host "Model       : $modelName"
Write-Host "Destination : $destination"

if ((Test-Path $destination) -and -not $Force) {
    $missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $destination $_)) }

    if ($missing.Count -eq 0) {
        Write-Host "Already present and complete, nothing to do." -ForegroundColor Green
        Write-Host "Use -Force to download again."
        return
    }

    Write-Warning "Model incomplete (missing: $($missing -join ', ')), downloading again."
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
        Write-Host ("Archive     : {0:N0} MB to download" -f [math]::Round($expectedBytes / 1MB))
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

            # Refreshing the bar on every block would cost more than the
            # download itself.
            if (([DateTime]::UtcNow - $lastReport).TotalMilliseconds -ge 500) {
                $lastReport = [DateTime]::UtcNow
                if ($expectedBytes) {
                    $percent = [math]::Min([math]::Round(($downloaded / $expectedBytes) * 100, 1), 100)
                    Write-Progress -Activity "Downloading $archiveName" `
                        -Status ("{0:N0} MB of {1:N0} MB" -f ($downloaded / 1MB), ($expectedBytes / 1MB)) `
                        -PercentComplete $percent
                }
            }
        }
    }
    finally {
        $target.Dispose()
        $source.Dispose()
        Write-Progress -Activity "Downloading $archiveName" -Completed
    }

    $actual = (Get-Item $archivePath).Length
    if ($expectedBytes -and ($actual -ne $expectedBytes)) {
        throw ("Incomplete download: {0:N0} bytes received out of {1:N0} expected." -f $actual, $expectedBytes)
    }

    Write-Host "Extracting..."

    # tar ships with Windows 10 and 11, and handles bzip2.
    & tar -xjf $archivePath -C $modelsDir
    if ($LASTEXITCODE -ne 0) {
        throw "Extraction failed (exit code $LASTEXITCODE)."
    }

    $missing = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $destination $_)) }
    if ($missing.Count -gt 0) {
        throw "Archive extracted but incomplete, missing: $($missing -join ', ')"
    }

    Write-Host ''
    Write-Host "Model installed." -ForegroundColor Green
    Write-Host ''
    Write-Host "Check the transcription chain with:"
    Write-Host "  dotnet build -c Release"
    Write-Host "  .\src\HexWin\bin\x64\Release\net9.0-windows\HexWin.exe --transcribe my-file.wav"
}
finally {
    $client.Dispose()
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    }
}
