#!/usr/bin/env bash
set -euo pipefail

# Resolve the repo root regardless of where this script is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

# Infra + app together, for a one-command spin-up. If you're running the
# API from Rider instead, use start-infra.sh directly and skip this file.
"$SCRIPT_DIR/start-infra.sh"

cat <<'EOF'
----------------------------------------------------------------------
  API (Swagger UI):      http://localhost:5049/swagger
  API (base URL):        http://localhost:5049
----------------------------------------------------------------------

EOF

echo "==> Starting the .NET API (dotnet run)... Ctrl+C to stop."
dotnet run
