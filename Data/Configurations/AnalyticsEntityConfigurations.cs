using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Analytics.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class AnalyticsSessionRowConfiguration : IEntityTypeConfiguration<AnalyticsSessionRow>
{
    public void Configure(EntityTypeBuilder<AnalyticsSessionRow> e)
    {
        e.ToTable("analytics_sessions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.SessionKey).HasMaxLength(64);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        e.Property(x => x.UserAgent).HasMaxLength(512);
        e.Property(x => x.FirstSeenAt);
        e.Property(x => x.LastSeenAt);
        e.HasIndex(x => x.SessionKey).IsUnique();
        e.HasIndex(x => x.FirstSeenAt);
    }
}

public sealed class AnalyticsPageViewRowConfiguration : IEntityTypeConfiguration<AnalyticsPageViewRow>
{
    public void Configure(EntityTypeBuilder<AnalyticsPageViewRow> e)
    {
        e.ToTable("analytics_page_views");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.SessionKey).HasMaxLength(64);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        e.Property(x => x.Path).HasMaxLength(512);
        e.Property(x => x.ViewedAt);
        e.HasIndex(x => x.ViewedAt);
        e.HasIndex(x => x.SessionKey);
        e.HasIndex(x => x.Path);
    }
}

public sealed class ProductViewEventRowConfiguration : IEntityTypeConfiguration<ProductViewEventRow>
{
    public void Configure(EntityTypeBuilder<ProductViewEventRow> e)
    {
        e.ToTable("product_view_events");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ProductId).HasMaxLength(64);
        e.Property(x => x.SessionKey).HasMaxLength(64);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        e.Property(x => x.ViewedAt);
        e.HasIndex(x => x.ViewedAt);
        e.HasIndex(x => x.ProductId);
    }
}
