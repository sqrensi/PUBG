$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $repoRoot ".runtime"
$serverPidFile = Join-Path $runtimeDir "dedicated-server.json"
$queuePidFile = Join-Path $runtimeDir "queue-service.json"
$serverExe = Join-Path $repoRoot "Build\Server\My project (12).exe"
$serverLog = Join-Path $repoRoot "server.log"
$queueLog = Join-Path $repoRoot "queue-service.log"
$queueDir = Join-Path $repoRoot "Backend\QueueService"

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

    Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
    return $null
}

function Save-PidInfo {
    param(
        [string]$Path,
        [System.Diagnostics.Process]$Process,
        [string]$Name
    )

    $payload = @{
        pid       = $Process.Id
        name      = $Name
        startedAt = (Get-Date).ToString("o")
    }
    $payload | ConvertTo-Json | Set-Content -Path $Path -Encoding UTF8
}

New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

if (!(Test-Path $serverExe)) {
    throw "Server build not found: $serverExe"
}

if (!(Test-Path $queueDir)) {
    throw "Queue service folder not found: $queueDir"
}

$serverProc = Get-AliveProcessFromFile -Path $serverPidFile
if ($null -eq $serverProc) {
    $serverProc = Start-Process -FilePath $serverExe `
        -ArgumentList @("-batchmode", "-nographics", "-logFile", $serverLog) `
        -WorkingDirectory (Split-Path -Parent $serverExe) `
        -PassThru
    Save-PidInfo -Path $serverPidFile -Process $serverProc -Name "dedicated-server"
    Write-Host "[start] Dedicated server started. pid=$($serverProc.Id)"
}
else {
    Write-Host "[start] Dedicated server already running. pid=$($serverProc.Id)"
}

$queueProc = Get-AliveProcessFromFile -Path $queuePidFile
if ($null -eq $queueProc) {
    Add-Content -Path $queueLog -Value ("`n=== QueueService start " + (Get-Date).ToString("o") + " ===")
    $queueProc = Start-Process -FilePath "cmd.exe" `
        -ArgumentList "/c", "set DEBUG_REALTIME=1&& npm.cmd start >> `"$queueLog`" 2>&1" `
        -WorkingDirectory $queueDir `
        -PassThru

    Start-Sleep -Milliseconds 600
    if ($queueProc.HasExited) {
        throw "Queue service process exited immediately. Check npm/node in $queueDir."
    }

    Save-PidInfo -Path $queuePidFile -Process $queueProc -Name "queue-service"
    Write-Host "[start] Queue service started. pid=$($queueProc.Id)"
}
else {
    Write-Host "[start] Queue service already running. pid=$($queueProc.Id)"
}

Write-Host "[start] Stack is ready."
