using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Features.Orders.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class OrderRowConfiguration : IEntityTypeConfiguration<OrderRow>
{
    public void Configure(EntityTypeBuilder<OrderRow> e)
    {
        e.ToTable("orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.PublicNumber).HasMaxLength(32);
        e.HasIndex(x => x.PublicNumber).IsUnique();
        e.Property(x => x.BuyerUserId).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.SellerUserId).HasMaxLength(64);
        e.Property(x => x.Status).HasMaxLength(32);
        e.Property(x => x.CustomerFirstName).HasMaxLength(256);
        e.Property(x => x.CustomerLastName).HasMaxLength(256);
        e.Property(x => x.PhonePrimary).HasMaxLength(64);
        e.Property(x => x.PhoneSecondary).HasMaxLength(64);
        e.Property(x => x.DeliveryMode).HasMaxLength(32);
        e.Property(x => x.DeliveryAddress).HasColumnType("text");
        e.Property(x => x.CurrencyCode).HasMaxLength(16);
        e.Property(x => x.Subtotal).HasColumnType("numeric(18,2)");
        e.Property(x => x.DeliveryFee).HasColumnType("numeric(18,2)");
        e.Property(x => x.Total).HasColumnType("numeric(18,2)");
        e.Property(x => x.PricePerKmSnapshot).HasColumnType("numeric(18,2)");
        e.Property(x => x.PaymentStatus).HasMaxLength(32);
        e.Property(x => x.PaymentMethod).HasMaxLength(64);
        e.Property(x => x.PaymentReference).HasMaxLength(96);
        e.Property(x => x.ClientEvidenceDecision).HasMaxLength(32);
        e.Property(x => x.ClientEvidenceUrls)
            .HasColumnName("ClientEvidenceUrlsJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.StringList())
            .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
        e.Property(x => x.ClientEvidenceNote).HasColumnType("text");
        e.Property(x => x.ClientEvidenceRejectReason).HasColumnType("text");
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.AffiliateCodeSnapshot).HasMaxLength(64);
        e.Property(x => x.AffiliateCommissionAmount).HasColumnType("numeric(18,2)");
        e.Property(x => x.InvalidatedReason).HasColumnType("text");
        e.HasIndex(x => x.BuyerUserId);
        e.HasIndex(x => x.StoreId);
        e.HasIndex(x => x.Status);
        e.Property(x => x.DeletedAtUtc);
        e.HasQueryFilter(o => o.DeletedAtUtc == null);
        e.HasMany(x => x.Lines)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderLineRowConfiguration : IEntityTypeConfiguration<OrderLineRow>
{
    public void Configure(EntityTypeBuilder<OrderLineRow> e)
    {
        e.ToTable("order_lines");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OrderId).HasMaxLength(64);
        e.Property(x => x.ProductId).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.ProductName).HasMaxLength(512);
        e.Property(x => x.TechnicalSpecs).HasColumnType("text");
        e.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)");
        e.Property(x => x.CurrencyCode).HasMaxLength(16);
        e.HasIndex(x => x.OrderId);
        e.HasIndex(x => x.ProductId);
    }
}
