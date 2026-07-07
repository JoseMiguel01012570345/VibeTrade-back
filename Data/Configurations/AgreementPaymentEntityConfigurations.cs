using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class TradeAgreementRowConfiguration : IEntityTypeConfiguration<TradeAgreementRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementRow> e)
    {
        e.ToTable("trade_agreements");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.Title).HasMaxLength(512);
        e.Property(x => x.IssuedByStoreId).HasMaxLength(64);
        e.Property(x => x.IssuerLabel).HasMaxLength(512);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.RespondedByUserId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteSheetUrl).HasColumnType("text");
        e.Property(x => x.DeletedByUserId).HasMaxLength(64);
        e.Property(x => x.HadBuyerAcceptance).HasColumnType("boolean");
        e.HasIndex(x => x.ThreadId);
        e.HasIndex(x => new { x.ThreadId, x.Status });
        e.HasIndex(x => x.DeletedAtUtc);
        e.HasMany(x => x.ServiceItems).WithOne(x => x.TradeAgreement).HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.ExtraFields).WithOne(x => x.TradeAgreement).HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TradeAgreementExtraFieldRowConfiguration : IEntityTypeConfiguration<TradeAgreementExtraFieldRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementExtraFieldRow> e)
    {
        e.ToTable("trade_agreement_extra_fields");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.Title).HasMaxLength(256);
        e.Property(x => x.ValueKind).HasMaxLength(16);
        e.Property(x => x.TextValue).HasColumnType("text");
        e.Property(x => x.MediaUrl).HasColumnType("text");
        e.Property(x => x.FileName).HasMaxLength(512);
        e.HasIndex(x => x.TradeAgreementId);
    }
}

public sealed class AgreementCurrencyPaymentRowConfiguration : IEntityTypeConfiguration<AgreementCurrencyPaymentRow>
{
    public void Configure(EntityTypeBuilder<AgreementCurrencyPaymentRow> e)
    {
        e.ToTable("agreement_currency_payments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.BuyerUserId).HasMaxLength(64);
        e.Property(x => x.Currency).HasMaxLength(16);
        e.Property(x => x.GatewayTransactionId).HasColumnName("StripePaymentIntentId").HasMaxLength(128);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.PaymentMethodId).HasColumnName("PaymentMethodStripeId").HasMaxLength(96);
        e.Property(x => x.PaymentErrorMessage).HasColumnName("StripeErrorMessage").HasColumnType("text");
        e.Property(x => x.ProcessorFeeAmountMinor).HasColumnName("StripeFeeAmountMinor");
        e.Property(x => x.ClientIdempotencyKey).HasMaxLength(200);
        e.Property(x => x.ClientSecretForConfirmation).HasColumnType("text");
        e.HasIndex(x => new { x.TradeAgreementId, x.ThreadId });
        e.HasIndex(x => new { x.TradeAgreementId, x.ClientIdempotencyKey }).IsUnique().HasDatabaseName("IX_agpay_agreement_idempotency").HasFilter("\"ClientIdempotencyKey\" IS NOT NULL");
        e.HasOne(x => x.TradeAgreement).WithMany().HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.RouteLegPaids).WithOne(x => x.AgreementCurrencyPayment).HasForeignKey(x => x.AgreementCurrencyPaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AgreementRouteLegPaidRowConfiguration : IEntityTypeConfiguration<AgreementRouteLegPaidRow>
{
    public void Configure(EntityTypeBuilder<AgreementRouteLegPaidRow> e)
    {
        e.ToTable("agreement_route_leg_paids");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.AgreementCurrencyPaymentId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.RouteStopId).HasMaxLength(96);
        e.HasIndex(x => new { x.RouteSheetId, x.RouteStopId });
    }
}

public sealed class AgreementServicePaymentRowConfiguration : IEntityTypeConfiguration<AgreementServicePaymentRow>
{
    public void Configure(EntityTypeBuilder<AgreementServicePaymentRow> e)
    {
        e.ToTable("agreement_service_payments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.BuyerUserId).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(64);
        e.Property(x => x.Currency).HasMaxLength(16);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.AgreementCurrencyPaymentId).HasMaxLength(64);
        e.Property(x => x.SellerPayoutPaymentMethodId).HasColumnName("SellerPayoutPaymentMethodStripeId").HasMaxLength(96);
        e.Property(x => x.SellerPayoutCardBrandSnapshot).HasMaxLength(32);
        e.Property(x => x.SellerPayoutCardLast4Snapshot).HasMaxLength(8);
        e.Property(x => x.SellerPayoutTransferId).HasColumnName("SellerPayoutStripeTransferId").HasMaxLength(128);
        e.HasIndex(x => new { x.TradeAgreementId, x.ThreadId });
        e.HasIndex(x => new { x.TradeAgreementId, x.ServiceItemId, x.EntryMonth, x.EntryDay, x.Currency }).IsUnique().HasDatabaseName("IX_agsp_unique_installment");
        e.HasOne(x => x.TradeAgreement).WithMany().HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.AgreementCurrencyPayment).WithMany().HasForeignKey(x => x.AgreementCurrencyPaymentId).OnDelete(DeleteBehavior.SetNull);
    }
}
