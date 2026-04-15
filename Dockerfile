FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VibeTrade.Backend.csproj", "./"]
RUN dotnet restore "VibeTrade.Backend.csproj"
COPY . .
RUN dotnet publish "VibeTrade.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql postgresql-contrib \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV POSTGRES_HOST=127.0.0.1
ENV POSTGRES_PORT=5432
ENV PGDATA=/var/lib/postgresql/data
COPY --from=build /app/publish .
COPY start.sh /app/start.sh

RUN chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]
