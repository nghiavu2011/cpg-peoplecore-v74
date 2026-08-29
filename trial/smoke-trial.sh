#!/usr/bin/env sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"
command -v curl >/dev/null 2>&1 || { echo "ERROR: curl is required for smoke-trial.sh" >&2; exit 2; }
KEY_FILE=trial/secrets/trial_auth_key.txt
[ -s "$KEY_FILE" ] || { echo "ERROR: Trial key missing. Run trial/run-trial.sh first." >&2; exit 2; }
KEY=$(cat "$KEY_FILE")
PORT=${PEOPLECORE_TRIAL_API_PORT:-8080}
BASE="http://127.0.0.1:${PORT}"
mkdir -p trial/evidence
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT="trial/evidence/local_trial_smoke_${STAMP}.log"
pass=0; fail=0
check() {
  name="$1" persona="$2" method="$3" path="$4" expected="$5" body="${6:-}"
  tmp=$(mktemp)
  if [ -n "$body" ]; then
    code=$(curl -sS -o "$tmp" -w '%{http_code}' -X "$method" -H "X-Trial-Staff-Code: $persona" -H "X-Trial-Key: $KEY" -H 'Content-Type: application/json' --data "$body" "$BASE$path" || true)
  else
    code=$(curl -sS -o "$tmp" -w '%{http_code}' -X "$method" -H "X-Trial-Staff-Code: $persona" -H "X-Trial-Key: $KEY" "$BASE$path" || true)
  fi
  if [ "$code" = "$expected" ]; then result=PASS; pass=$((pass+1)); else result=FAIL; fail=$((fail+1)); fi
  printf '%s | %s | %s %s | expected=%s actual=%s\n' "$result" "$name" "$method" "$path" "$expected" "$code" | tee -a "$OUT"
  if [ "$result" = FAIL ]; then sed 's/[[:cntrl:]]//g' "$tmp" | head -c 2000 | tee -a "$OUT"; printf '\n' | tee -a "$OUT"; fi
  rm -f "$tmp"
}

health=$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/health/startup" || true)
if [ "$health" = 200 ]; then echo "PASS | startup-health | expected=200 actual=$health" | tee -a "$OUT"; pass=$((pass+1)); else echo "FAIL | startup-health | expected=200 actual=$health" | tee -a "$OUT"; fail=$((fail+1)); fi
for p in TRIAL-EMP TRIAL-MGR TRIAL-HR TRIAL-PAY TRIAL-ADMIN; do check "identity-$p" "$p" GET /api/v1/identity/me 200; done
check employee-cannot-platform TRIAL-EMP GET /api/platform/status 403
check admin-platform TRIAL-ADMIN GET /api/platform/status 200
check manager-pending-leave TRIAL-MGR GET /api/v1/leave/pending-for-me 200
check employee-project-validation TRIAL-EMP POST /api/v1/timesheets/validate-project 200 '{"workDate":"2026-08-31","projectCode":"DEMO-001"}'
check payroll-preview TRIAL-PAY POST /api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/preview 200
check payroll-prelive-guard TRIAL-PAY POST /api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/prelive-safety-check 200
check payroll-release-blocked TRIAL-PAY POST /api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/release 400
check employee-payroll-denied TRIAL-EMP POST /api/v1/payslips/employees/22222222-2222-4222-8222-222222222222/2026-08/preview 403
printf 'RESULT pass=%s fail=%s evidence=%s\n' "$pass" "$fail" "$OUT" | tee -a "$OUT"
[ "$fail" -eq 0 ]
