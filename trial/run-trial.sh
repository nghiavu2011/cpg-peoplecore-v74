#!/usr/bin/env sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"
command -v docker >/dev/null 2>&1 || { echo "ERROR: Docker is required." >&2; exit 2; }
docker compose version >/dev/null 2>&1 || { echo "ERROR: Docker Compose v2 is required." >&2; exit 2; }
mkdir -p trial/secrets
chmod 700 trial/secrets 2>/dev/null || true
make_secret() {
  file="$1"
  if [ ! -s "$file" ]; then
    if command -v openssl >/dev/null 2>&1; then openssl rand -hex 32 > "$file";
    elif [ -r /dev/urandom ]; then od -An -N32 -tx1 /dev/urandom | tr -d ' \n' > "$file";
    else echo "ERROR: secure random source unavailable." >&2; exit 3; fi
    chmod 600 "$file" 2>/dev/null || true
  fi
}
make_secret trial/secrets/postgres_password.txt
make_secret trial/secrets/trial_auth_key.txt

echo "Starting CPG PeopleCore V74-RC2 LOCAL TRIAL..."
docker compose -f docker-compose.trial.yml up -d --build

PORT=${PEOPLECORE_TRIAL_API_PORT:-8080}
URL="http://127.0.0.1:${PORT}"
i=0
while [ "$i" -lt 90 ]; do
  if command -v curl >/dev/null 2>&1 && curl -fsS "$URL/health/startup" >/dev/null 2>&1; then break; fi
  i=$((i+1)); sleep 2
done
if command -v curl >/dev/null 2>&1 && ! curl -fsS "$URL/health/startup" >/dev/null 2>&1; then
  echo "ERROR: API did not become startup-healthy. Showing logs:" >&2
  docker compose -f docker-compose.trial.yml logs --tail=200 api migrate postgres >&2
  exit 4
fi
KEY=$(cat trial/secrets/trial_auth_key.txt)
printf '\nTRIAL READY\n===========\nURL: %s/trial/\nHealth: %s/health/startup\nTrial key: %s\n\nPersonas: TRIAL-EMP | TRIAL-MGR | TRIAL-HR | TRIAL-PAY | TRIAL-ADMIN\n\nWARNING: LOCAL TRIAL ONLY. This is NOT Entra/BRAVO/UAT/production evidence.\n' "$URL" "$URL" "$KEY"
