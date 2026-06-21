$ErrorActionPreference = "Stop"

# 此腳本位於 ...\backup\scripts\
# $PSScriptRoot = ...\backup\scripts
# $BackupRoot   = ...\backup
# $RepoRoot     = ...          (用來找 docker-compose.yml)
$BackupRoot  = Split-Path $PSScriptRoot -Parent
$RepoRoot    = Split-Path $BackupRoot -Parent
$BackupDir   = Join-Path $BackupRoot "data"
$ComposeFile = Join-Path $RepoRoot "docker-compose.yml"
$ImageName   = "mcr.microsoft.com/mssql/server:2019-latest"
$VolumeName  = "docker-mssql2019_mssql2019-data"
$TempDir     = Join-Path $env:TEMP "mssql-backup-$(Get-Random)"

if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }
New-Item -ItemType Directory -Path $TempDir | Out-Null

$wasRunning = $false
try {
    Write-Host "[1/3] Stopping container for consistent backup..." -ForegroundColor Cyan
    $running = docker ps --filter "name=mssql-2019-dev" --filter "status=running" -q
    if ($running) {
        $wasRunning = $true
        docker compose -f $ComposeFile stop | Out-Null
    } else {
        Write-Host "  Container not running, skipping stop." -ForegroundColor Gray
    }

    Write-Host "[2/3] Exporting volume data to tar.gz (staging via temp)..." -ForegroundColor Cyan
    docker run --rm -v "${VolumeName}:/source" -v "${TempDir}:/dest" alpine `
        tar czf /dest/mssql-volume.tar.gz -C /source .
    if ($LASTEXITCODE -ne 0) { throw "Volume export failed" }
    Move-Item (Join-Path $TempDir "mssql-volume.tar.gz") `
              (Join-Path $BackupDir "mssql-volume.tar.gz") -Force

    Write-Host "[3/3] Saving image to tar (this can take a few minutes)..." -ForegroundColor Cyan
    docker save $ImageName -o (Join-Path $BackupDir "mssql-image.tar")
    if ($LASTEXITCODE -ne 0) { throw "Image save failed" }
}
finally {
    if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
    if ($wasRunning) {
        Write-Host "Restarting container..." -ForegroundColor Cyan
        docker compose -f $ComposeFile start | Out-Null
    }
}

Write-Host ""
Write-Host "Backup complete!" -ForegroundColor Green
Get-ChildItem $BackupDir | Format-Table Name, @{N='Size(MB)';E={[math]::Round($_.Length/1MB,1)}}, LastWriteTime
