FROM docker.elastic.co/elasticsearch/elasticsearch:8.17.3

# Production-like defaults:
# - Security ON (requires setting ELASTIC_PASSWORD in Render env vars)
# - Single-node (no clustering)
ENV discovery.type=single-node
ENV xpack.security.enabled=true
ENV xpack.security.enrollment.enabled=false

# Optional: keep memory low for small instances (override in Render env vars).
ENV ES_JAVA_OPTS="-Xms512m -Xmx512m"

COPY elasticsearch.yml /usr/share/elasticsearch/config/elasticsearch.yml

EXPOSE 9200

