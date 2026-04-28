FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VibeTrade.Backend.csproj", "./"]
RUN dotnet restore "VibeTrade.Backend.csproj"
COPY . .
RUN dotnet publish "VibeTrade.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

EXPOSE 8080
COPY --from=build /app/publish .
COPY start.sh /app/start.sh

RUN sed -i 's/\r$//' /app/start.sh && chmod +x /app/start.sh

ENTRYPOINT ["/app/start.sh"]
