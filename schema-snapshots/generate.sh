#!/usr/bin/env bash
set -euo pipefail
command -v node >/dev/null || { echo "Node.js is required." >&2; exit 1; }
exec node "$(dirname "$0")/generate.mjs" "$@"
