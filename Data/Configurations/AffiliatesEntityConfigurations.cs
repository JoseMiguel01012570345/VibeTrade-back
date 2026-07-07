using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Affiliates.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class AffiliateRowConfiguration : IEntityTypeConfiguration<AffiliateRow>
{
    public void Configure(EntityTypeBuilder<AffiliateRow> e)
    {
        e.ToTable("affiliates");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.Code).HasMaxLength(64);
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.OwnerUserId).HasMaxLength(64);
        e.Property(x => x.DisplayName).HasMaxLength(256);
        e.Property(x => x.CommissionKind).HasMaxLength(16);
        e.Property(x => x.CommissionValue).HasColumnType("numeric(18,2)");
        e.Property(x => x.CommissionCurrencyCode).HasMaxLength(16);
        e.HasIndex(x => x.OwnerUserId);
    }
}
