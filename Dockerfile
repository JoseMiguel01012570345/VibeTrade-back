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

EXPOSE 8080
COPY --from=build /app/publish .
COPY start.sh /app/start.sh
COPY elasticsearch.yml /app/elasticsearch.yml

RUN chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]
