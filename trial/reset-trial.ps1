param([switch]$Yes)
$ErrorActionPreference = 'Stop'
if (-not $Yes) { throw 'This deletes ONLY local trial data. Re-run: .\trial\reset-trial.ps1 -Yes' }
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
docker compose -f docker-compose.trial.yml down -v --remove-orphans
Remove-Item -Force -ErrorAction SilentlyContinue trial\secrets\postgres_password.txt, trial\secrets\trial_auth_key.txt
Write-Host 'Local trial data and generated trial secrets removed.'
