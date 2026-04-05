using Microsoft.EntityFrameworkCore;

namespace VibeTrade.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WeatherForecastRow> WeatherForecasts => Set<WeatherForecastRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeatherForecastRow>().ToTable("WeatherForecasts");
    }
}
