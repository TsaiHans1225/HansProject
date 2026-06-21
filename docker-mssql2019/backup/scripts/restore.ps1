$ErrorActionPreference = "Stop"

# 此腳本位於 ...\backup\scripts\
$BackupRoot  = Split-Path $PSScriptRoot -Parent
$RepoRoot    = Split-Path $BackupRoot -Parent
$BackupDir   = Join-Path $BackupRoot "data"
$ComposeFile = Join-Path $RepoRoot "docker-compose.yml"
$VolumeName  = "docker-mssql2019_mssql2019-data"
$ImageTar    = Join-Path $BackupDir "mssql-image.tar"
$VolumeTar   = Join-Path $BackupDir "mssql-volume.tar.gz"
$TempDir     = Join-Path $env:TEMP "mssql-restore-$(Get-Random)"

if (-not (Test-Path $ImageTar))  { Write-Error "Image tar not found: $ImageTar" }
if (-not (Test-Path $VolumeTar)) { Write-Error "Volume tar not found: $VolumeTar" }

$existing = docker volume ls --filter "name=^${VolumeName}$" -q
if ($existing) {
    Write-Host ""
    Write-Host "WARNING: Volume '$VolumeName' already exists on this machine." -ForegroundColor Yellow
    $resp = Read-Host "Overwrite existing data? Type YES to continue"
    if ($resp -ne "YES") { Write-Host "Aborted."; exit 1 }
    Write-Host "Removing existing container and volume..." -ForegroundColor Yellow
    docker compose -f $ComposeFile down | Out-Null
    docker volume rm $VolumeName | Out-Null
}

New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    Write-Host "[1/3] Loading image from tar (this can take a few minutes)..." -ForegroundColor Cyan
    docker load -i $ImageTar
    if ($LASTEXITCODE -ne 0) { throw "Image load failed" }

    Write-Host "[2/3] Creating volume and restoring data (staging via temp)..." -ForegroundColor Cyan
    docker volume create $VolumeName | Out-Null
    Copy-Item $VolumeTar (Join-Path $TempDir "mssql-volume.tar.gz")
    docker run --rm -v "${VolumeName}:/dest" -v "${TempDir}:/src" alpine `
        tar xzf /src/mssql-volume.tar.gz -C /dest
    if ($LASTEXITCODE -ne 0) { throw "Volume restore failed" }

    Write-Host "[3/3] Starting container..." -ForegroundColor Cyan
    docker compose -f $ComposeFile up -d
    if ($LASTEXITCODE -ne 0) { throw "Compose up failed" }
}
finally {
    if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
}

Write-Host ""
Write-Host "Restore complete!" -ForegroundColor Green
docker compose -f $ComposeFile ps
