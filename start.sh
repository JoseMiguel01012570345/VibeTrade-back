#!/usr/bin/env bash
set -euo pipefail

# Render sets PORT; default to 8080 locally/in docker.
export ASPNETCORE_URLS="http://+:${PORT:-8080}"

# Database: use a managed Postgres (e.g. Render) or docker-compose `postgres` service.
# Set POSTGRES_HOST, POSTGRES_PORT, POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD in the environment.

term_handler() {
  if [[ -n "${app_pid:-}" ]]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
}

trap term_handler TERM INT

echo "Starting VibeTrade backend (binding ${ASPNETCORE_URLS})..."
dotnet VibeTrade.Backend.dll &
app_pid=$!
wait "$app_pid"
exit $?
