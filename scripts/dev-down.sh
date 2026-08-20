#!/usr/bin/env bash
set -euo pipefail

# Resolve the repo root regardless of where this script is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

# If dev-up.sh (or a plain `dotnet run`) is still running in another
# terminal, stop it too — stop-infra.sh alone would leave the API process
# behind, still holding the port open.
echo "==> Stopping the .NET API if it's running..."
# `dotnet run` on Linux builds and launches a native apphost binary (not
# `dotnet exec ...dll`), so the running process's command line is just the
# executable path itself — match on that.
pkill -f "bin/Debug/net8.0/OpenSearchLearningLab$" 2>/dev/null && echo "    stopped." || echo "    not running."

"$SCRIPT_DIR/stop-infra.sh" "$@"
