using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Debts.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class WarehouseDebtRowConfiguration : IEntityTypeConfiguration<WarehouseDebtRow>
{
    public void Configure(EntityTypeBuilder<WarehouseDebtRow> e)
    {
        e.ToTable("warehouse_debts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.OrderId).HasMaxLength(64);
        e.Property(x => x.OrderPublicNumber).HasMaxLength(32);
        e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        e.Property(x => x.CurrencyCode).HasMaxLength(16);
        e.HasIndex(x => x.StoreId);
        e.HasIndex(x => x.OrderId);
        e.HasIndex(x => new { x.Liquidated, x.Deleted });
    }
}

public sealed class AffiliateDebtRowConfiguration : IEntityTypeConfiguration<AffiliateDebtRow>
{
    public void Configure(EntityTypeBuilder<AffiliateDebtRow> e)
    {
        e.ToTable("affiliate_debts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.AffiliateId).HasMaxLength(64);
        e.Property(x => x.AffiliateCode).HasMaxLength(64);
        e.Property(x => x.OrderId).HasMaxLength(64);
        e.Property(x => x.OrderPublicNumber).HasMaxLength(32);
        e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        e.Property(x => x.CurrencyCode).HasMaxLength(16);
        e.HasIndex(x => x.AffiliateCode);
        e.HasIndex(x => x.OrderId);
        e.HasIndex(x => new { x.Liquidated, x.Deleted });
    }
}

public sealed class CarrierDebtRowConfiguration : IEntityTypeConfiguration<CarrierDebtRow>
{
    public void Configure(EntityTypeBuilder<CarrierDebtRow> e)
    {
        e.ToTable("carrier_debts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.CarrierUserId).HasMaxLength(64);
        e.Property(x => x.OrderId).HasMaxLength(64);
        e.Property(x => x.OrderPublicNumber).HasMaxLength(32);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.Property(x => x.RatePerKm).HasColumnType("numeric(18,2)");
        e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        e.Property(x => x.CurrencyCode).HasMaxLength(16);
        e.HasIndex(x => x.CarrierUserId);
        e.HasIndex(x => x.OrderId);
        e.HasIndex(x => new { x.Liquidated, x.Deleted });
    }
}
