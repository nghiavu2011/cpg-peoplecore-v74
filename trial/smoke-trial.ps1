$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$keyPath = Join-Path $Root 'trial\secrets\trial_auth_key.txt'
if (-not (Test-Path $keyPath)) { throw 'Trial key missing. Run .\trial\run-trial.ps1 first.' }
$key = (Get-Content $keyPath -Raw).Trim()
$port = if ($env:PEOPLECORE_TRIAL_API_PORT) { $env:PEOPLECORE_TRIAL_API_PORT } else { '8080' }
$base = "http://127.0.0.1:$port"
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'trial\evidence') | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$out = Join-Path $Root "trial\evidence\local_trial_smoke_$stamp.log"
$script:pass = 0; $script:fail = 0
function Invoke-TrialRequest([string]$Persona,[string]$Method,[string]$Path,[object]$Body=$null) {
  $headers = @{ 'X-Trial-Staff-Code'=$Persona; 'X-Trial-Key'=$key }
  try {
    $args = @{ Uri="$base$Path"; Method=$Method; Headers=$headers; UseBasicParsing=$true }
    if ($null -ne $Body) { $args.ContentType='application/json'; $args.Body=($Body | ConvertTo-Json -Compress -Depth 10) }
    $r = Invoke-WebRequest @args
    return @{ Status=[int]$r.StatusCode; Content=$r.Content }
  } catch {
    $status = 0; $content = $_.Exception.Message
    if ($_.Exception.Response) {
      try { $status = [int]$_.Exception.Response.StatusCode } catch { }
      try {
        $stream = $_.Exception.Response.GetResponseStream()
        if ($stream) { $reader = New-Object IO.StreamReader($stream); $content = $reader.ReadToEnd(); $reader.Dispose() }
      } catch { }
    }
    return @{ Status=$status; Content=$content }
  }
}
function Test-Call([string]$Name,[string]$Persona,[string]$Method,[string]$Path,[int]$Expected,[object]$Body=$null) {
  $res = Invoke-TrialRequest $Persona $Method $Path $Body
  $actual = [int]$res.Status
  $result = if ($actual -eq $Expected) { $script:pass++; 'PASS' } else { $script:fail++; 'FAIL' }
  $line = "$result | $Name | $Method $Path | expected=$Expected actual=$actual"
  $line | Tee-Object -FilePath $out -Append
  if ($result -eq 'FAIL' -and $res.Content) { ([string]$res.Content).Substring(0,[Math]::Min(2000,([string]$res.Content).Length)) | Add-Content $out }
}
try { $h=Invoke-WebRequest -Uri "$base/health/startup" -UseBasicParsing; $hc=[int]$h.StatusCode } catch { $hc=0 }
if ($hc -eq 200) { $script:pass++; "PASS | startup-health | expected=200 actual=$hc" | Tee-Object -FilePath $out -Append } else { $script:fail++; "FAIL | startup-health | expected=200 actual=$hc" | Tee-Object -FilePath $out -Append }
'TRIAL-EMP','TRIAL-MGR','TRIAL-HR','TRIAL-PAY','TRIAL-ADMIN' | ForEach-Object { Test-Call "identity-$_" $_ GET '/api/v1/identity/me' 200 }
Test-Call employee-cannot-platform TRIAL-EMP GET '/api/platform/status' 403
Test-Call admin-platform TRIAL-ADMIN GET '/api/platform/status' 200
Test-Call manager-pending-leave TRIAL-MGR GET '/api/v1/leave/pending-for-me' 200
Test-Call employee-project-validation TRIAL-EMP POST '/api/v1/timesheets/validate-project' 200 @{workDate='2026-08-31';projectCode='DEMO-001'}
Test-Call payroll-preview TRIAL-PAY POST '/api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/preview' 200
Test-Call payroll-prelive-guard TRIAL-PAY POST '/api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/prelive-safety-check' 200
Test-Call payroll-release-blocked TRIAL-PAY POST '/api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/release' 400
Test-Call employee-payroll-denied TRIAL-EMP POST '/api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/preview' 403
"RESULT pass=$script:pass fail=$script:fail evidence=$out" | Tee-Object -FilePath $out -Append
if ($script:fail -gt 0) { exit 1 }
