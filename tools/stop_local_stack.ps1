$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $repoRoot ".runtime"
$serverPidFile = Join-Path $runtimeDir "dedicated-server.json"
$queuePidFile = Join-Path $runtimeDir "queue-service.json"

function Get-AliveProcessFromFile {
    param([string]$Path)

    if (!(Test-Path $Path)) {
        return $null
    }

    try {
        $info = Get-Content -Path $Path -Raw | ConvertFrom-Json
        if ($null -ne $info -and $null -ne $info.pid) {
            $pidValue = [int]$info.pid
            $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if ($null -ne $proc) {
                return $proc
            }
        }
    }
    catch {
        # ignore malformed file
    }

    return $null
}

function Stop-TrackedProcess {
    param(
        [string]$PidFile,
        [string]$DisplayName
    )

    $proc = Get-AliveProcessFromFile -Path $PidFile
    if ($null -eq $proc) {
        Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
        Write-Host "[stop] $DisplayName not running."
        return
    }

    Write-Host "[stop] Stopping $DisplayName pid=$($proc.Id)..."
    & taskkill /PID $proc.Id /T /F | Out-Null
    Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
}

Stop-TrackedProcess -PidFile $queuePidFile -DisplayName "Queue service"
Stop-TrackedProcess -PidFile $serverPidFile -DisplayName "Dedicated server"

Write-Host "[stop] Stack stopped."
