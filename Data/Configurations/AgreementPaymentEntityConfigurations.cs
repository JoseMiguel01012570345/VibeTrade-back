using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;

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
        e.HasMany(x => x.MerchandiseLines).WithOne(x => x.TradeAgreement).HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.MerchandiseMeta).WithOne(x => x.TradeAgreement).HasForeignKey<TradeAgreementMerchandiseMetaRow>(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
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

public sealed class TradeAgreementMerchandiseLineRowConfiguration : IEntityTypeConfiguration<TradeAgreementMerchandiseLineRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementMerchandiseLineRow> e)
    {
        e.ToTable("trade_agreement_merchandise_lines");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.LinkedStoreProductId).HasMaxLength(64);
        e.Property(x => x.Tipo).HasMaxLength(512);
        e.Property(x => x.Cantidad).HasMaxLength(128);
        e.Property(x => x.ValorUnitario).HasMaxLength(128);
        e.Property(x => x.Estado).HasMaxLength(32);
        e.Property(x => x.Descuento).HasMaxLength(128);
        e.Property(x => x.Impuestos).HasMaxLength(128);
        e.Property(x => x.Moneda).HasMaxLength(32);
        e.Property(x => x.TipoEmbalaje).HasMaxLength(256);
        e.Property(x => x.DevolucionesDesc).HasColumnType("text");
        e.Property(x => x.DevolucionQuienPaga).HasMaxLength(256);
        e.Property(x => x.DevolucionPlazos).HasMaxLength(256);
        e.Property(x => x.Regulaciones).HasColumnType("text");
        e.HasIndex(x => x.TradeAgreementId);
    }
}

public sealed class TradeAgreementMerchandiseMetaRowConfiguration : IEntityTypeConfiguration<TradeAgreementMerchandiseMetaRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementMerchandiseMetaRow> e)
    {
        e.ToTable("trade_agreement_merchandise_metas");
        e.HasKey(x => x.TradeAgreementId);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.Moneda).HasMaxLength(32);
        e.Property(x => x.TipoEmbalaje).HasMaxLength(256);
        e.Property(x => x.DevolucionesDesc).HasColumnType("text");
        e.Property(x => x.DevolucionQuienPaga).HasMaxLength(256);
        e.Property(x => x.DevolucionPlazos).HasMaxLength(256);
        e.Property(x => x.Regulaciones).HasColumnType("text");
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
        e.Property(x => x.StripePaymentIntentId).HasMaxLength(128);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.PaymentMethodStripeId).HasMaxLength(96);
        e.Property(x => x.StripeErrorMessage).HasColumnType("text");
        e.Property(x => x.ClientIdempotencyKey).HasMaxLength(200);
        e.Property(x => x.ClientSecretForConfirmation).HasColumnType("text");
        e.HasIndex(x => new { x.TradeAgreementId, x.ThreadId });
        e.HasIndex(x => new { x.TradeAgreementId, x.ClientIdempotencyKey }).IsUnique().HasDatabaseName("IX_agpay_agreement_idempotency").HasFilter("\"ClientIdempotencyKey\" IS NOT NULL");
        e.HasOne(x => x.TradeAgreement).WithMany().HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.RouteLegPaids).WithOne(x => x.AgreementCurrencyPayment).HasForeignKey(x => x.AgreementCurrencyPaymentId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.MerchandiseLinePaids).WithOne(x => x.AgreementCurrencyPayment).HasForeignKey(x => x.AgreementCurrencyPaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AgreementMerchandiseLinePaidRowConfiguration : IEntityTypeConfiguration<AgreementMerchandiseLinePaidRow>
{
    public void Configure(EntityTypeBuilder<AgreementMerchandiseLinePaidRow> e)
    {
        e.ToTable("agreement_merchandise_line_paids");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.AgreementCurrencyPaymentId).HasMaxLength(64);
        e.Property(x => x.MerchandiseLineId).HasMaxLength(64);
        e.Property(x => x.Currency).HasMaxLength(16);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.BuyerUserId).HasMaxLength(64);
        e.Property(x => x.Status).HasMaxLength(32);
        e.HasIndex(x => new { x.MerchandiseLineId, x.Currency });
        e.HasIndex(x => new { x.ThreadId, x.Status });
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
        e.Property(x => x.SellerPayoutPaymentMethodStripeId).HasMaxLength(96);
        e.Property(x => x.SellerPayoutCardBrandSnapshot).HasMaxLength(32);
        e.Property(x => x.SellerPayoutCardLast4Snapshot).HasMaxLength(8);
        e.Property(x => x.SellerPayoutStripeTransferId).HasMaxLength(128);
        e.HasIndex(x => new { x.TradeAgreementId, x.ThreadId });
        e.HasIndex(x => new { x.TradeAgreementId, x.ServiceItemId, x.EntryMonth, x.EntryDay, x.Currency }).IsUnique().HasDatabaseName("IX_agsp_unique_installment");
        e.HasOne(x => x.TradeAgreement).WithMany().HasForeignKey(x => x.TradeAgreementId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.AgreementCurrencyPayment).WithMany().HasForeignKey(x => x.AgreementCurrencyPaymentId).OnDelete(DeleteBehavior.SetNull);
    }
}
