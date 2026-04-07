# VibeTrade.Backend

ASP.NET Core minimal API (.NET 9) for the VibeTrade backend.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) installed (`dotnet --version` should report 9.x).
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or another Docker engine) if you use the included **PostgreSQL** container.

## Docker (API + PostgreSQL)

1. Copy **`.env.example`** to **`.env`** and set a strong **`POSTGRES_PASSWORD`** (the repo’s **`.env`** is gitignored). Compose injects these values into the **API** container; the image does not embed secrets.

2. Build and start **PostgreSQL** and the **API**:

   ```powershell
   docker compose up -d --build
   ```

   The API listens on **http://localhost:5110** (override host port with **`API_HTTP_PORT`** in `.env` if needed). Inside the stack, the API uses **`POSTGRES_HOST=postgres`** to reach the database.

   Both services use **`restart: unless-stopped`**, so when Docker starts they come back automatically. The API runs **EF Core migrations on each startup** (with short retries if PostgreSQL is not ready yet).

3. Optional: apply migrations from your machine (without starting the API), e.g. Postgres is up in Docker:

   ```powershell
   dotnet ef database update --project VibeTrade.Backend.csproj
   ```

**Database only** (run the API on the host with `dotnet run`): `docker compose up -d postgres`.

### Persistencia de PostgreSQL (no perder datos al reiniciar Docker)

- Los datos viven en el volumen Docker **nombrado** `vibetrade_pgdata` (definido en `docker-compose.yml`). **Reiniciar** el daemon de Docker o los contenedores (`docker compose restart`, `docker compose up -d`) **no** borra ese volumen.
- **No** uses `docker compose down -v` ni `docker volume rm` si querés conservar la base: el flag **`-v`** elimina los volúmenes declarados en el compose y con eso se pierde la data.
- Si antes tenías un volumen con prefijo de proyecto (`mi_carpeta_vibetrade_pgdata`) y al actualizar el compose aparece una BD vacía, es porque ahora el volumen fijo es `vibetrade_pgdata`. Podés migrar datos copiando desde el volumen antiguo o dejando el volumen antiguo montado manualmente; en instalaciones nuevas no hace falta.

## Start the project

1. Open a terminal and go to the repository root (this folder).

2. Restore dependencies (optional; `run` and `build` restore automatically if needed):

   ```powershell
   dotnet restore
   ```

3. Run the API using one of the launch profiles:

   **HTTP only** (recommended for local checks):

   ```powershell
   dotnet run --launch-profile http
   ```

   The app listens on **http://localhost:5110**.

   **HTTPS + HTTP** (uses the development certificate for HTTPS):

   ```powershell
   dotnet run --launch-profile https
   ```

   The app listens on **https://localhost:7239** and **http://localhost:5110**.

## Other useful commands

Build without running:

```powershell
dotnet build
```

Sample endpoint (JSON): **GET** `http://localhost:5110/weatherforecast`

## Troubleshooting

**Build error: cannot copy `VibeTrade.Backend.exe` / file is in use**

Another instance of the app is still running. Stop it (Ctrl+C in the run terminal), or:

```powershell
Stop-Process -Name VibeTrade.Backend -Force
```

**HTTPS redirect warning in the console**

If you run the **http** profile only, you may see a warning about HTTPS redirection. It is safe to ignore for local HTTP testing, or use the **https** profile.

**Browser shows “Not secure” or “Your connection is not private” for `https://localhost`**

The **https** profile uses the ASP.NET Core **HTTPS development certificate**. It is issued to `localhost` and is effectively self-signed, so the browser distrusts it until the cert is trusted on your machine.

Trust it once (a Windows prompt will ask for confirmation—choose **Yes**):

```powershell
dotnet dev-certs https --trust
```

Then fully close the tab or restart the browser and open `https://localhost:7239` again.

Verify trust status:

```powershell
dotnet dev-certs https --check --trust
```

If the warning persists, recreate and trust:

```powershell
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

**Alternative:** use the **http** profile and `http://localhost:5110` (no TLS) for local API checks.
