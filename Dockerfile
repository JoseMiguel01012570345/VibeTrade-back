FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VibeTrade.Backend.csproj", "./"]
RUN dotnet restore "VibeTrade.Backend.csproj"
COPY . .
RUN dotnet publish "VibeTrade.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql postgresql-contrib ca-certificates curl gnupg openjdk-17-jre-headless \
    && rm -rf /var/lib/apt/lists/*

# Pre-install Elasticsearch so container boot does not block on curl (Render expects PORT bound quickly).
ARG ES_VERSION=8.17.3
RUN useradd -r -s /usr/sbin/nologin -d /usr/share/elasticsearch elasticsearch \
    && mkdir -p /usr/share \
    && curl -fsSL "https://artifacts.elastic.co/downloads/elasticsearch/elasticsearch-${ES_VERSION}-linux-x86_64.tar.gz" -o /tmp/elasticsearch.tgz \
    && tar -xzf /tmp/elasticsearch.tgz -C /usr/share \
    && rm -f /tmp/elasticsearch.tgz \
    && mv "/usr/share/elasticsearch-${ES_VERSION}" /usr/share/elasticsearch \
    && chown -R elasticsearch:elasticsearch /usr/share/elasticsearch

EXPOSE 8080
COPY --from=build /app/publish .
COPY start.sh /app/start.sh
COPY elasticsearch.yml /app/elasticsearch.yml

RUN chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]
