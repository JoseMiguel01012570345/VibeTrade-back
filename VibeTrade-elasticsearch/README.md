# VibeTrade Elasticsearch (separate container)

This folder is meant to be deployed as its **own Render Docker Web Service** (separate from `VibeTrade-back`).

## Render setup (recommended)

- **Service type**: Web Service
- **Runtime**: Docker
- **Root directory**: `VibeTrade-elasticsearch`
- **Health check**: `GET /` on port `9200` (default ES root endpoint)

## Production-like configuration (security ON)

This service runs Elasticsearch with **security enabled**. You must set a password for the built-in `elastic` user.

In Render, set the following **environment variables** on the Elasticsearch service:

- `ELASTIC_PASSWORD` (required; store as a secret)
- `ES_JAVA_OPTS` (required on 512Mi; recommended always)
  - For 512Mi: `-Xms256m -Xmx256m`
  - For 1Gi+: `-Xms512m -Xmx512m` (or higher depending on your plan)

### Networking

For production, prefer **private networking** so Elasticsearch is not exposed publicly.

### Persistent disk (important)

Elasticsearch stores indices on disk. Add a Render disk and mount it to:

- **Mount path**: `/usr/share/elasticsearch/data`

## Backend configuration

Point your backend to this service via environment variables:

- `Elasticsearch__Enabled=true`
- `Elasticsearch__Uri=http://<elasticsearch-service-host>:9200`

Where `<elasticsearch-service-host>` is the hostname Render provides for service-to-service calls
(prefer private networking if available).

### Backend auth

Set one of these on the backend service:

- **Basic Auth** (recommended for simplicity):
  - `Elasticsearch__Username=elastic`
  - `Elasticsearch__Password=<same value as ELASTIC_PASSWORD>`
- **API key** (preferred long-term):
  - `Elasticsearch__ApiKey=<api key>`

## Notes

- This setup keeps **HTTP TLS disabled** (`xpack.security.http.ssl.enabled=false`) and assumes Elasticsearch
  is only reachable over private networking. If you expose ES publicly, enable TLS and configure certs.
- To reduce memory use on small instances, `xpack.ml.enabled` is disabled in `elasticsearch.yml`.

