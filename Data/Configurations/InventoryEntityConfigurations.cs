using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Inventory.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class StoreCategoryRowConfiguration : IEntityTypeConfiguration<StoreCategoryRow>
{
    public void Configure(EntityTypeBuilder<StoreCategoryRow> e)
    {
        e.ToTable("store_categories");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.Name).HasMaxLength(256);
        e.Property(x => x.ParentCategoryId).HasMaxLength(64);
        e.HasOne(x => x.Parent)
            .WithMany()
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.StoreId);
        e.HasIndex(x => new { x.StoreId, x.Name });
    }
}

public sealed class StoreSupplierRowConfiguration : IEntityTypeConfiguration<StoreSupplierRow>
{
    public void Configure(EntityTypeBuilder<StoreSupplierRow> e)
    {
        e.ToTable("store_suppliers");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.BusinessName).HasMaxLength(512);
        e.Property(x => x.PortalUsername).HasMaxLength(128);
        e.Property(x => x.PasswordHash).HasMaxLength(512);
        e.Property(x => x.PlatformDebtAmount).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        e.Property(x => x.PlatformDebtCurrencyCode).HasMaxLength(16).HasDefaultValue("USD");
        e.HasIndex(x => x.StoreId);
        e.HasIndex(x => x.PortalUsername).IsUnique();
    }
}

public sealed class StoreBannerRowConfiguration : IEntityTypeConfiguration<StoreBannerRow>
{
    public void Configure(EntityTypeBuilder<StoreBannerRow> e)
    {
        e.ToTable("store_banners");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.MediaUrl).HasColumnType("text");
        e.HasIndex(x => new { x.StoreId, x.Kind, x.Active });
    }
}
