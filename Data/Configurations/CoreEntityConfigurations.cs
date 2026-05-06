using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class MarketWorkspaceRowConfiguration : IEntityTypeConfiguration<MarketWorkspaceRow>
{
    public void Configure(EntityTypeBuilder<MarketWorkspaceRow> e)
    {
        e.ToTable("market_workspaces");
        e.HasKey(x => x.Id);
        e.Property(x => x.State)
            .HasColumnName("Payload")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.MarketWorkspace())
            .Metadata.SetValueComparer(EntityValueConversions.MarketWorkspaceComparer());
        e.Property(x => x.UpdatedAt);
    }
}

public sealed class StoredMediaRowConfiguration : IEntityTypeConfiguration<StoredMediaRow>
{
    public void Configure(EntityTypeBuilder<StoredMediaRow> e)
    {
        e.ToTable("stored_media");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.MimeType).HasMaxLength(256);
        e.Property(x => x.FileName).HasMaxLength(512);
        e.Property(x => x.SizeBytes);
        e.Property(x => x.Bytes).HasColumnType("bytea");
        e.Property(x => x.CreatedAt);
    }
}

public sealed class UserOfferInteractionRowConfiguration : IEntityTypeConfiguration<UserOfferInteractionRow>
{
    public void Configure(EntityTypeBuilder<UserOfferInteractionRow> e)
    {
        e.ToTable("user_offer_interactions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.UserId).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.EventType).HasMaxLength(32);
        e.Property(x => x.CreatedAt);
        e.HasIndex(x => x.UserId);
        e.HasIndex(x => x.OfferId);
        e.HasIndex(x => x.CreatedAt);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasIndex(x => new { x.OfferId, x.CreatedAt });
    }
}
