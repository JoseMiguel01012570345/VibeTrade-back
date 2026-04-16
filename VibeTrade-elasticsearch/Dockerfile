FROM docker.elastic.co/elasticsearch/elasticsearch:8.17.3

# Production-like defaults:
# - Security ON (requires setting ELASTIC_PASSWORD in Render env vars)
# - Single-node (no clustering)
ENV discovery.type=single-node
ENV xpack.security.enabled=true
ENV xpack.security.enrollment.enabled=false

# Low-memory default (Render 512Mi will OOM with larger heaps).
# Override in Render env vars if you have more RAM.
ENV ES_JAVA_OPTS="-Xms256m -Xmx256m"

COPY elasticsearch.yml /usr/share/elasticsearch/config/elasticsearch.yml

EXPOSE 9200

