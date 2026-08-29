$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop / Docker Engine is required.' }
docker compose version | Out-Null
$SecretDir = Join-Path $Root 'trial\secrets'
New-Item -ItemType Directory -Force -Path $SecretDir | Out-Null
function New-RandomHexSecret([string]$Path) {
  if ((Test-Path $Path) -and ((Get-Item $Path).Length -gt 0)) { return }
  $bytes = New-Object byte[] 32
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
  $hex = ([BitConverter]::ToString($bytes) -replace '-','').ToLowerInvariant()
  [IO.File]::WriteAllText($Path, $hex)
}
New-RandomHexSecret (Join-Path $SecretDir 'postgres_password.txt')
New-RandomHexSecret (Join-Path $SecretDir 'trial_auth_key.txt')
Write-Host 'Starting CPG PeopleCore V74-RC2 LOCAL TRIAL...'
docker compose -f docker-compose.trial.yml up -d --build
$port = if ($env:PEOPLECORE_TRIAL_API_PORT) { $env:PEOPLECORE_TRIAL_API_PORT } else { '8080' }
$url = "http://127.0.0.1:$port"
$ok = $false
for ($i=0; $i -lt 90; $i++) {
  try { Invoke-RestMethod -Uri "$url/health/startup" -TimeoutSec 3 | Out-Null; $ok = $true; break } catch { Start-Sleep -Seconds 2 }
}
if (-not $ok) {
  docker compose -f docker-compose.trial.yml logs --tail=200 api migrate postgres
  throw 'API did not become startup-healthy.'
}
$key = (Get-Content (Join-Path $SecretDir 'trial_auth_key.txt') -Raw).Trim()
Write-Host ''
Write-Host 'TRIAL READY'
Write-Host "URL: $url/trial/"
Write-Host "Health: $url/health/startup"
Write-Host "Trial key: $key"
Write-Host 'Personas: TRIAL-EMP | TRIAL-MGR | TRIAL-HR | TRIAL-PAY | TRIAL-ADMIN'
Write-Warning 'LOCAL TRIAL ONLY. This is NOT Entra/BRAVO/UAT/production evidence.'
