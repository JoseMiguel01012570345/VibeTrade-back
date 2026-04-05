using Npgsql;

namespace VibeTrade.Backend.Infrastructure;

public static class PostgresConfiguration
{
    public static string BuildConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "vibetrade";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "vibetrade";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
            ?? throw new InvalidOperationException(
                "POSTGRES_PASSWORD is not set. Add it to a .env file in the project root (see .env.example).");

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
