using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Routing.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class RouteBackgroundJobRowConfiguration : IEntityTypeConfiguration<RouteBackgroundJobRow>
{
    public void Configure(EntityTypeBuilder<RouteBackgroundJobRow> e)
    {
        e.ToTable("route_background_jobs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.JobType).HasMaxLength(64);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.LastError).HasColumnType("text");
        e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        e.HasIndex(x => new { x.ThreadId, x.RouteSheetId });
    }
}

public sealed class RouteSheetRouteCalculationRowConfiguration : IEntityTypeConfiguration<RouteSheetRouteCalculationRow>
{
    public void Configure(EntityTypeBuilder<RouteSheetRouteCalculationRow> e)
    {
        e.ToTable("route_sheet_route_calculations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.MatrixJson).HasColumnType("jsonb");
        e.Property(x => x.VisitOrderJson).HasColumnType("jsonb");
        e.Property(x => x.LastError).HasColumnType("text");
        e.HasIndex(x => new { x.ThreadId, x.RouteSheetId }).IsUnique();
    }
}
