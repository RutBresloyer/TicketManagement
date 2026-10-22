#!/usr/bin/env bash
# One-shot local database setup: start SQL Server, apply migrations, create the least-privilege runtime user,
# seed the deterministic 100K demo dataset. Safe to re-run (every step is idempotent).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [ ! -f .env ]; then
  echo "Missing .env - run: cp .env.example .env  (then set the passwords)" >&2
  exit 1
fi

echo "==> Starting SQL Server (docker compose)"
docker compose up -d --wait sqlserver

# shellcheck source=dev-env.sh
source scripts/dev-env.sh

echo "==> Applying EF Core migrations"
dotnet run --project backend/RequestManagement.Api --launch-profile http -- migrate

echo "==> Creating least-privilege runtime user rm_app"
RM_APP_PASSWORD="$(sed -n 's/^RM_APP_PASSWORD=//p' .env)"
export RM_APP_PASSWORD   # visible only to this script and the docker exec below, never echoed
docker exec -i -e RM_APP_PASSWORD rm-sqlserver bash -c \
  'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /dev/stdin' \
  < scripts/sql/create-runtime-user.sql
unset RM_APP_PASSWORD

echo "==> Seeding demo data (idempotent)"
dotnet run --project backend/RequestManagement.Api --launch-profile http -- seed

echo "==> Done. Start the API with:"
echo "    source scripts/dev-env.sh && dotnet run --project backend/RequestManagement.Api --launch-profile http"
