using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> e)
    {
        e.ToTable("user_accounts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.PhoneDigits).HasMaxLength(32);
        e.Property(x => x.DisplayName).HasMaxLength(256);
        e.Property(x => x.Email).HasMaxLength(320);
        e.Property(x => x.PhoneDisplay).HasMaxLength(64);
        e.Property(x => x.AvatarUrl).HasColumnType("text");
        e.Property(x => x.Instagram).HasMaxLength(256);
        e.Property(x => x.Telegram).HasMaxLength(256);
        e.Property(x => x.XAccount).HasMaxLength(256);
        e.Property(x => x.StripeCustomerId).HasMaxLength(96);
        e.Property(x => x.StripeConnectedAccountId).HasMaxLength(64);
        e.Property(x => x.SavedOfferIds)
            .HasColumnName("SavedOfferIdsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.HasIndex(x => x.PhoneDigits)
            .IsUnique()
            .HasFilter("\"PhoneDigits\" IS NOT NULL");
        e.HasMany(x => x.Stores)
            .WithOne(x => x.Owner)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StoreRowConfiguration : IEntityTypeConfiguration<StoreRow>
{
    public void Configure(EntityTypeBuilder<StoreRow> e)
    {
        e.ToTable("stores");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OwnerUserId).HasMaxLength(64);
        e.Property(x => x.Name).HasMaxLength(512);
        e.Property(x => x.NormalizedName).HasMaxLength(512);
        e.Property(x => x.AvatarUrl).HasColumnType("text");
        e.Property(x => x.Categories)
            .HasColumnName("CategoriesJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.Pitch);
        e.Property(x => x.WebsiteUrl).HasMaxLength(2048);
        e.HasIndex(x => x.OwnerUserId);
        e.Property(x => x.DeletedAtUtc);
        e.HasQueryFilter(s => s.DeletedAtUtc == null);
        e.HasIndex(x => x.NormalizedName)
            .IsUnique()
            .HasFilter("\"NormalizedName\" IS NOT NULL AND \"DeletedAtUtc\" IS NULL");
        e.HasMany(x => x.Products)
            .WithOne(x => x.Store)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Services)
            .WithOne(x => x.Store)
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StoreProductRowConfiguration : IEntityTypeConfiguration<StoreProductRow>
{
    public void Configure(EntityTypeBuilder<StoreProductRow> e)
    {
        e.ToTable("store_products");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.MonedaPrecio).HasMaxLength(16);
        e.Property(x => x.Monedas)
            .HasColumnName("MonedasJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.PhotoUrls)
            .HasColumnName("PhotoUrlsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.CustomFields)
            .HasColumnName("CustomFieldsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.CustomFields())
            .Metadata.SetValueComparer(EntityValueConversions.CustomFieldsComparer());
        e.Property(x => x.OfferQa)
            .HasColumnName("OfferQaJson")
            .HasColumnType("jsonb")
            .HasConversion(OfferQaJson.CreateEfConverter());
        e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
        e.Property(x => x.DeletedAtUtc);
        e.HasQueryFilter(p => p.DeletedAtUtc == null);
        e.HasIndex(x => x.StoreId);
    }
}

public sealed class StoreServiceRowConfiguration : IEntityTypeConfiguration<StoreServiceRow>
{
    public void Configure(EntityTypeBuilder<StoreServiceRow> e)
    {
        e.ToTable("store_services");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.Riesgos)
            .HasColumnName("RiesgosJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceRiesgos())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceRiesgosComparer());
        e.Property(x => x.Dependencias)
            .HasColumnName("DependenciasJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceDependencias())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceDependenciasComparer());
        e.Property(x => x.Garantias)
            .HasColumnName("GarantiasJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceGarantias())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceGarantiasComparer());
        e.Property(x => x.Monedas)
            .HasColumnName("MonedasJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.CustomFields)
            .HasColumnName("CustomFieldsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.CustomFields())
            .Metadata.SetValueComparer(EntityValueConversions.CustomFieldsComparer());
        e.Property(x => x.PhotoUrls)
            .HasColumnName("PhotoUrlsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.OfferQa)
            .HasColumnName("OfferQaJson")
            .HasColumnType("jsonb")
            .HasConversion(OfferQaJson.CreateEfConverter());
        e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
        e.Property(x => x.DeletedAtUtc);
        e.HasQueryFilter(s => s.DeletedAtUtc == null);
        e.HasIndex(x => x.StoreId);
    }
}
