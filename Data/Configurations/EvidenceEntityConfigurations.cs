using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class ServiceEvidenceRowConfiguration : IEntityTypeConfiguration<ServiceEvidenceRow>
{
    public void Configure(EntityTypeBuilder<ServiceEvidenceRow> e)
    {
        e.ToTable("service_evidences");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.AgreementServicePaymentId).HasMaxLength(64);
        e.Property(x => x.SellerUserId).HasMaxLength(64);
        e.Property(x => x.Text).HasColumnType("text");
        e.Property(x => x.LastSubmittedText).HasColumnType("text");
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.Attachments)
            .HasColumnName("AttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.Property(x => x.LastSubmittedAttachments)
            .HasColumnName("LastSubmittedAttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.HasIndex(x => x.AgreementServicePaymentId).IsUnique();
        e.HasOne(x => x.AgreementServicePayment).WithMany().HasForeignKey(x => x.AgreementServicePaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MerchandiseEvidenceRowConfiguration : IEntityTypeConfiguration<MerchandiseEvidenceRow>
{
    public void Configure(EntityTypeBuilder<MerchandiseEvidenceRow> e)
    {
        e.ToTable("merchandise_evidences");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.AgreementMerchandiseLinePaidId).HasMaxLength(64);
        e.Property(x => x.SellerUserId).HasMaxLength(64);
        e.Property(x => x.Text).HasColumnType("text");
        e.Property(x => x.LastSubmittedText).HasColumnType("text");
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.Attachments)
            .HasColumnName("AttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.Property(x => x.LastSubmittedAttachments)
            .HasColumnName("LastSubmittedAttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.HasIndex(x => x.AgreementMerchandiseLinePaidId).IsUnique();
        e.HasOne(x => x.AgreementMerchandiseLinePaid).WithMany().HasForeignKey(x => x.AgreementMerchandiseLinePaidId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CarrierDeliveryEvidenceRowConfiguration : IEntityTypeConfiguration<CarrierDeliveryEvidenceRow>
{
    public void Configure(EntityTypeBuilder<CarrierDeliveryEvidenceRow> e)
    {
        e.ToTable("carrier_delivery_evidences");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.Property(x => x.CarrierUserId).HasMaxLength(64);
        e.Property(x => x.Text).HasColumnType("text");
        e.Property(x => x.LastSubmittedText).HasColumnType("text");
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.DecidedByUserId).HasMaxLength(64);
        e.Property(x => x.Attachments)
            .HasColumnName("AttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.Property(x => x.LastSubmittedAttachments)
            .HasColumnName("LastSubmittedAttachmentsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.ServiceEvidenceAttachments())
            .Metadata.SetValueComparer(EntityValueConversions.ServiceEvidenceAttachmentsComparer());
        e.HasIndex(x => new { x.ThreadId, x.TradeAgreementId, x.RouteSheetId, x.RouteStopId }).IsUnique();
        e.HasIndex(x => new { x.ThreadId, x.Status });
    }
}
