using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Data;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        Env.Load(Path.Combine(root, ".env"));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresConfiguration.BuildConnectionString())
            .Options;

        return new AppDbContext(options);
    }
}
