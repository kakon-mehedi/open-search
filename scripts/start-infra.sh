#!/usr/bin/env bash
set -euo pipefail

# Resolve the repo root regardless of where this script is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

# Infra only — PostgreSQL + OpenSearch + OpenSearch Dashboards. Does NOT
# run the .NET API; run/debug that from Rider (or `dotnet run`) yourself.
echo "==> Starting infrastructure (PostgreSQL, OpenSearch, OpenSearch Dashboards)..."
docker compose up -d

echo "==> Waiting for OpenSearch to accept requests..."
until curl -s -o /dev/null -w '%{http_code}' http://localhost:9200 2>/dev/null | grep -q 200; do
  sleep 2
  echo "    still waiting for OpenSearch..."
done
echo "==> OpenSearch is up."

cat <<'EOF'

----------------------------------------------------------------------
  OpenSearch:             http://localhost:9200
  OpenSearch Dashboards:  http://localhost:5601
  PostgreSQL:             localhost:5433

  PostgreSQL creds:  db=opensearchlab  user=postgres  password=postgres

  Auth for OpenSearch / Dashboards: NONE. The security plugin is
  disabled for this lab (docker-compose.yml sets
  plugins.security.disabled=true and
  DISABLE_SECURITY_DASHBOARDS_PLUGIN=true) — open the Dashboards URL
  directly, no username/password prompt.

  Infra is up — now run/debug the API from Rider (Program.cs), or
  `dotnet run` from the repo root. appsettings.json already points at
  these ports.
----------------------------------------------------------------------

EOF
