#!/usr/bin/env bash
set -euo pipefail

# Resolve the repo root regardless of where this script is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

# Infra only — does not touch the .NET API (that's however you started it:
# Rider, `dotnet run`, or dev-up.sh).
echo "==> Stopping infrastructure (PostgreSQL, OpenSearch, OpenSearch Dashboards)..."
if [[ "${1:-}" == "-v" || "${1:-}" == "--volumes" ]]; then
  echo "    (--volumes passed: also deleting PostgreSQL/OpenSearch data)"
  docker compose down --volumes
else
  docker compose down
fi

echo "==> Done. Data volumes were kept — pass -v/--volumes to also wipe them."
