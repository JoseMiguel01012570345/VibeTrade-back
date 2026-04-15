#!/usr/bin/env bash
set -euo pipefail

# Render sets PORT; default to 8080 locally/in docker.
export ASPNETCORE_URLS="http://+:${PORT:-8080}"

# Optional: start Elasticsearch inside the container (dev only).
# Enable with START_ELASTICSEARCH=true. Binds to 127.0.0.1 so it's private to the container.
START_ELASTICSEARCH="${START_ELASTICSEARCH:-false}"
es_started="false"
if [[ "${START_ELASTICSEARCH}" == "1" || "${START_ELASTICSEARCH}" == "true" || "${START_ELASTICSEARCH}" == "TRUE" ]]; then
  ES_VERSION="${ES_VERSION:-8.17.3}"
  ES_HOME="${ES_HOME:-/usr/share/elasticsearch}"
  ES_DATA="${ES_DATA:-/var/lib/elasticsearch}"
  ES_LOGS="${ES_LOGS:-/var/log/elasticsearch}"
  ES_PORT="${ES_PORT:-9200}"

  if [[ ! -x "${ES_HOME}/bin/elasticsearch" ]]; then
    echo "Installing Elasticsearch ${ES_VERSION}..."
    mkdir -p /usr/share
    curl -fsSL "https://artifacts.elastic.co/downloads/elasticsearch/elasticsearch-${ES_VERSION}-linux-x86_64.tar.gz" -o /tmp/elasticsearch.tgz
    tar -xzf /tmp/elasticsearch.tgz -C /usr/share
    rm -f /tmp/elasticsearch.tgz
    mv "/usr/share/elasticsearch-${ES_VERSION}" "${ES_HOME}"
  fi

  if ! id -u elasticsearch >/dev/null 2>&1; then
    useradd -r -s /usr/sbin/nologin -d "${ES_HOME}" elasticsearch
  fi

  mkdir -p "${ES_DATA}" "${ES_LOGS}"
  chown -R elasticsearch:elasticsearch "${ES_HOME}" "${ES_DATA}" "${ES_LOGS}" /app/elasticsearch.yml

  echo "Starting Elasticsearch on 127.0.0.1:${ES_PORT}..."
  su elasticsearch -s /bin/bash -c "export ES_PATH_CONF='/app'; '${ES_HOME}/bin/elasticsearch' -Epath.data='${ES_DATA}' -Epath.logs='${ES_LOGS}' -Ehttp.port='${ES_PORT}'" >/dev/null 2>&1 &
  es_pid=$!

  echo "Waiting for Elasticsearch to be ready..."
  for _ in {1..60}; do
    if curl -fsS "http://127.0.0.1:${ES_PORT}/" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done

  if ! curl -fsS "http://127.0.0.1:${ES_PORT}/" >/dev/null 2>&1; then
    echo "Elasticsearch did not become ready in time."
    exit 1
  fi

  export Elasticsearch__Enabled=true
  export Elasticsearch__Uri="http://127.0.0.1:${ES_PORT}"
  es_started="true"
fi

# Optional: start a local Postgres inside the container (useful for local/dev only).
START_POSTGRES="${START_POSTGRES:-false}"
postgres_started="false"
if [[ "${START_POSTGRES}" == "1" || "${START_POSTGRES}" == "true" || "${START_POSTGRES}" == "TRUE" ]]; then
  export POSTGRES_HOST="${POSTGRES_HOST:-127.0.0.1}"
  export POSTGRES_PORT="${POSTGRES_PORT:-5432}"
  export POSTGRES_DB="${POSTGRES_DB:-vibetrade}"
  export POSTGRES_USER="${POSTGRES_USER:-vibetrade}"
  export PGDATA="${PGDATA:-/var/lib/postgresql/data}"

  if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
    echo "POSTGRES_PASSWORD is required when START_POSTGRES=true."
    exit 1
  fi

  POSTGRES_BIN_DIR="$(find /usr/lib/postgresql -mindepth 2 -maxdepth 2 -type d -name bin | head -n 1)"
  if [[ -z "$POSTGRES_BIN_DIR" ]]; then
    echo "Could not find PostgreSQL binaries under /usr/lib/postgresql."
    exit 1
  fi
  export PATH="$POSTGRES_BIN_DIR:$PATH"
  echo "Using PostgreSQL binaries from: $POSTGRES_BIN_DIR"

  mkdir -p "$PGDATA"
  chown -R postgres:postgres "$PGDATA"
  chmod 700 "$PGDATA"
  echo "Using PGDATA at: $PGDATA"

  if [[ ! -s "$PGDATA/PG_VERSION" ]]; then
    echo "Initializing PostgreSQL data directory..."
    su postgres -s /bin/bash -c "initdb -D '$PGDATA'"
  fi

  echo "Starting PostgreSQL on ${POSTGRES_HOST}:${POSTGRES_PORT}..."
  su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -o \"-p $POSTGRES_PORT -h $POSTGRES_HOST\" -w start"

  echo "Waiting for PostgreSQL to accept connections..."
  until su postgres -s /bin/bash -c "psql -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -d postgres -c '\q'" >/dev/null 2>&1; do
    sleep 1
  done

  if ! su postgres -s /bin/bash -c "psql -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -d postgres -tAc \"SELECT 1 FROM pg_roles WHERE rolname = '$POSTGRES_USER'\"" | grep -q 1; then
    echo "Creating PostgreSQL role: $POSTGRES_USER"
    su postgres -s /bin/bash -c "psql -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -d postgres -c \"CREATE USER \\\"$POSTGRES_USER\\\" WITH PASSWORD '$POSTGRES_PASSWORD';\""
  fi

  if ! su postgres -s /bin/bash -c "psql -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -d postgres -tAc \"SELECT 1 FROM pg_database WHERE datname = '$POSTGRES_DB'\"" | grep -q 1; then
    echo "Creating PostgreSQL database: $POSTGRES_DB"
    su postgres -s /bin/bash -c "psql -h '$POSTGRES_HOST' -p '$POSTGRES_PORT' -d postgres -c \"CREATE DATABASE \\\"$POSTGRES_DB\\\" OWNER \\\"$POSTGRES_USER\\\";\""
  fi

  postgres_started="true"
fi

term_handler() {
  if [[ -n "${app_pid:-}" ]]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  if [[ "${es_started}" == "true" && -n "${es_pid:-}" ]]; then
    kill -TERM "${es_pid}" 2>/dev/null || true
    wait "${es_pid}" 2>/dev/null || true
  fi
  if [[ "${postgres_started}" == "true" ]]; then
    su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -m fast stop" || true
  fi
}

trap term_handler TERM INT

echo "Starting VibeTrade backend..."
dotnet VibeTrade.Backend.dll &
app_pid=$!
wait "$app_pid"
app_status=$?

if [[ "${postgres_started}" == "true" ]]; then
  su postgres -s /bin/bash -c "pg_ctl -D '$PGDATA' -m fast stop" || true
fi
exit "$app_status"

