using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class OfferLikeRowConfiguration : IEntityTypeConfiguration<OfferLikeRow>
{
    public void Configure(EntityTypeBuilder<OfferLikeRow> e)
    {
        e.ToTable("offer_likes");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.LikerKey).HasMaxLength(96);
        e.Property(x => x.CreatedAtUtc);
        e.HasIndex(x => x.OfferId);
        e.HasIndex(x => new { x.OfferId, x.LikerKey }).IsUnique();
        e.HasIndex(x => x.LikerKey);
    }
}

public sealed class OfferQaCommentLikeRowConfiguration : IEntityTypeConfiguration<OfferQaCommentLikeRow>
{
    public void Configure(EntityTypeBuilder<OfferQaCommentLikeRow> e)
    {
        e.ToTable("offer_qa_comment_likes");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.QaCommentId).HasMaxLength(64);
        e.Property(x => x.LikerKey).HasMaxLength(96);
        e.Property(x => x.CreatedAtUtc);
        e.HasIndex(x => new { x.OfferId, x.QaCommentId });
        e.HasIndex(x => new { x.OfferId, x.QaCommentId, x.LikerKey }).IsUnique();
        e.HasIndex(x => x.LikerKey);
    }
}

public sealed class TrustScoreLedgerRowConfiguration : IEntityTypeConfiguration<TrustScoreLedgerRow>
{
    public void Configure(EntityTypeBuilder<TrustScoreLedgerRow> e)
    {
        e.ToTable("trust_score_ledger");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.SubjectType).HasMaxLength(16);
        e.Property(x => x.SubjectId).HasMaxLength(64);
        e.Property(x => x.Reason).HasMaxLength(512);
        e.HasIndex(x => new { x.SubjectType, x.SubjectId, x.CreatedAtUtc });
    }
}

public sealed class RouteStopDeliveryRowConfiguration : IEntityTypeConfiguration<RouteStopDeliveryRow>
{
    public void Configure(EntityTypeBuilder<RouteStopDeliveryRow> e)
    {
        e.ToTable("route_stop_deliveries");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.Property(x => x.State).HasMaxLength(40);
        e.Property(x => x.CurrentOwnerUserId).HasMaxLength(64);
        e.Property(x => x.RefundEligibleReason).HasMaxLength(32);
        e.HasIndex(x => new { x.ThreadId, x.TradeAgreementId, x.RouteSheetId, x.RouteStopId }).IsUnique();
        e.HasIndex(x => new { x.ThreadId, x.State });
        e.HasIndex(x => x.CurrentOwnerUserId);
        e.HasOne<ChatThreadRow>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CarrierOwnershipEventRowConfiguration : IEntityTypeConfiguration<CarrierOwnershipEventRow>
{
    public void Configure(EntityTypeBuilder<CarrierOwnershipEventRow> e)
    {
        e.ToTable("carrier_ownership_events");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.Property(x => x.CarrierUserId).HasMaxLength(64);
        e.Property(x => x.Action).HasMaxLength(16);
        e.Property(x => x.Reason).HasMaxLength(128);
        e.HasIndex(x => new { x.ThreadId, x.AtUtc });
        e.HasIndex(x => new { x.RouteSheetId, x.RouteStopId, x.AtUtc });
        e.HasOne<ChatThreadRow>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CarrierTelemetrySampleRowConfiguration : IEntityTypeConfiguration<CarrierTelemetrySampleRow>
{
    public void Configure(EntityTypeBuilder<CarrierTelemetrySampleRow> e)
    {
        e.ToTable("carrier_telemetry_samples");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.Property(x => x.CarrierUserId).HasMaxLength(64);
        e.Property(x => x.SourceClientId).HasMaxLength(128);
        e.HasIndex(x => new { x.RouteStopId, x.ReportedAtUtc });
        e.HasIndex(x => x.ThreadId);
        e.HasOne<ChatThreadRow>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
    }
}
