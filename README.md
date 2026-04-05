# VibeTrade.Backend

ASP.NET Core minimal API (.NET 9) for the VibeTrade backend.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) installed (`dotnet --version` should report 9.x).

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
