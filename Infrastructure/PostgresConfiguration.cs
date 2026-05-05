using Npgsql;

namespace VibeTrade.Backend.Infrastructure;

public static class PostgresConfiguration
{
    public static string BuildConnectionString()
    {
        var integrationTest = Environment.GetEnvironmentVariable("VIBETRADE_INTEGRATION_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(integrationTest))
            return integrationTest.Trim();

        // Prefer a full URL if provided (Render-style).
        // Supported env vars: POSTGRES_URL, DATABASE_URL.
        var url =
            Environment.GetEnvironmentVariable("POSTGRES_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
                throw new InvalidOperationException(
                    "Invalid PostgreSQL URL. Expected postgres:// or postgresql:// in POSTGRES_URL (or DATABASE_URL).");

            var dbFromPath = uri.AbsolutePath.Trim('/');
            var userInfo = uri.UserInfo.Split(':', 2);
            var usernameFromUrl = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? "");
            var passwordFromUrl = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "");

            if (string.IsNullOrWhiteSpace(usernameFromUrl)
                || string.IsNullOrWhiteSpace(passwordFromUrl)
                || string.IsNullOrWhiteSpace(dbFromPath))
                throw new InvalidOperationException("PostgreSQL URL is missing username, password, or database name.");

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Database = dbFromPath,
                Username = usernameFromUrl,
                Password = passwordFromUrl,
                SslMode = SslMode.Require,
            };

            return builder.ConnectionString;
        }

        // Fallback: individual POSTGRES_* vars.
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "vibetrade";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "vibetrade";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
            ?? throw new InvalidOperationException(
                "POSTGRES_PASSWORD is not set. Set POSTGRES_* vars or provide POSTGRES_URL.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var p) ? p : 5432,
            Database = database,
            Username = username,
            Password = password,
        }.ConnectionString;
    }
}
