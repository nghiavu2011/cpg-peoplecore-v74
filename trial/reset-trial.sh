#!/usr/bin/env sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"
[ "${1:-}" = "--yes" ] || { echo "This deletes ONLY the local trial PostgreSQL volume. Re-run: trial/reset-trial.sh --yes"; exit 2; }
docker compose -f docker-compose.trial.yml down -v --remove-orphans
rm -f trial/secrets/postgres_password.txt trial/secrets/trial_auth_key.txt
echo "Local trial data and generated trial secrets removed."
