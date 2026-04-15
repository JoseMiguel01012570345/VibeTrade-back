#!/usr/bin/env bash
set -euo pipefail

export POSTGRES_HOST=127.0.0.1
export POSTGRES_PORT="${POSTGRES_PORT:-5432}"
export POSTGRES_DB="${POSTGRES_DB:-vibetrade}"
export POSTGRES_USER="${POSTGRES_USER:-vibetrade}"
export PGDATA="${PGDATA:-/var/lib/postgresql/data}"

if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
  echo "POSTGRES_PASSWORD is required."
  exit 1
fi

export PATH="$(echo /usr/lib/postgresql/*/bin):$PATH"

mkdir -p "$PGDATA"
chown -R postgres:postgres "$PGDATA"

if [[ ! -s "$PGDATA/PG_VERSION" ]]; then
  su postgres -s /bin/bash -c "initdb -D '$PGDATA'"
fi

su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -o \"-p $POSTGRES_PORT -h 127.0.0.1\" -w start"

until su postgres -s /bin/bash -c "psql -h 127.0.0.1 -p '$POSTGRES_PORT' -d postgres -c '\q'" >/dev/null 2>&1; do
  sleep 1
done

if ! su postgres -s /bin/bash -c "psql -h 127.0.0.1 -p '$POSTGRES_PORT' -d postgres -tAc \"SELECT 1 FROM pg_roles WHERE rolname = '$POSTGRES_USER'\"" | grep -q 1; then
  su postgres -s /bin/bash -c "psql -h 127.0.0.1 -p '$POSTGRES_PORT' -d postgres -c \"CREATE USER \\\"$POSTGRES_USER\\\" WITH PASSWORD '$POSTGRES_PASSWORD';\""
fi

if ! su postgres -s /bin/bash -c "psql -h 127.0.0.1 -p '$POSTGRES_PORT' -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname = '$POSTGRES_DB'\"" | grep -q 1; then
  su postgres -s /bin/bash -c "psql -h 127.0.0.1 -p '$POSTGRES_PORT' -d postgres -c \"CREATE DATABASE \\\"$POSTGRES_DB\\\" OWNER \\\"$POSTGRES_USER\\\";\""
fi

term_handler() {
  if [[ -n "${app_pid:-}" ]]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -m fast stop" || true
}

trap term_handler TERM INT

dotnet VibeTrade.Backend.dll &
app_pid=$!
wait "$app_pid"
app_status=$?

su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -m fast stop" || true
exit "$app_status"
