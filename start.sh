#!/usr/bin/env bash
set -euo pipefail

# Render sets PORT; default to 8080 locally/in docker.
export ASPNETCORE_URLS="http://+:${PORT:-8080}"

# Database: use a managed Postgres (e.g. Render) or docker-compose `postgres` service.
# Set POSTGRES_HOST, POSTGRES_PORT, POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD in the environment.

# Optional: Elasticsearch in-container (dev). MUST NOT block Kestrel — Render scans PORT quickly.
START_ELASTICSEARCH="${START_ELASTICSEARCH:-false}"
es_wrapper_pid=""
if [[ "${START_ELASTICSEARCH}" == "1" || "${START_ELASTICSEARCH}" == "true" || "${START_ELASTICSEARCH}" == "TRUE" ]]; then
  ES_VERSION="${ES_VERSION:-8.17.3}"
  ES_HOME="${ES_HOME:-/usr/share/elasticsearch}"
  ES_DATA="${ES_DATA:-/var/lib/elasticsearch}"
  ES_LOGS="${ES_LOGS:-/var/log/elasticsearch}"
  ES_PORT="${ES_PORT:-9200}"
  # Small defaults for Render free/small instances (override with ES_JAVA_OPTS).
  export ES_JAVA_OPTS="${ES_JAVA_OPTS:--Xms256m -Xmx512m}"

  export Elasticsearch__Enabled=true
  export Elasticsearch__Uri="http://127.0.0.1:${ES_PORT}"

  (
    set +e
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

    echo "Starting Elasticsearch on 127.0.0.1:${ES_PORT} (background)..."
    exec su elasticsearch -s /bin/bash -c "export ES_PATH_CONF='/app'; export ES_JAVA_OPTS='${ES_JAVA_OPTS}'; '${ES_HOME}/bin/elasticsearch' -Epath.data='${ES_DATA}' -Epath.logs='${ES_LOGS}' -Ehttp.port='${ES_PORT}'"
  ) &
  es_wrapper_pid=$!
fi

term_handler() {
  if [[ -n "${app_pid:-}" ]]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  if [[ -n "${es_wrapper_pid:-}" ]]; then
    kill -TERM "$es_wrapper_pid" 2>/dev/null || true
    wait "$es_wrapper_pid" 2>/dev/null || true
  fi
}

trap term_handler TERM INT

echo "Starting VibeTrade backend (binding ${ASPNETCORE_URLS})..."
dotnet VibeTrade.Backend.dll &
app_pid=$!
wait "$app_pid"
exit $?
