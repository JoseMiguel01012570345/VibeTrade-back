using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class ChatThreadRowConfiguration : IEntityTypeConfiguration<ChatThreadRow>
{
    public void Configure(EntityTypeBuilder<ChatThreadRow> e)
    {
        e.ToTable("chat_threads");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.StoreId).HasMaxLength(64);
        e.Property(x => x.BuyerUserId).HasMaxLength(64);
        e.Property(x => x.SellerUserId).HasMaxLength(64);
        e.Property(x => x.InitiatorUserId).HasMaxLength(64);
        e.Property(x => x.FirstMessageSentAtUtc);
        e.Property(x => x.PurchaseMode);
        e.Property(x => x.CreatedAtUtc);
        e.Property(x => x.DeletedAtUtc);
        e.Property(x => x.BuyerExpelledAtUtc);
        e.Property(x => x.SellerExpelledAtUtc);
        e.Property(x => x.PartyExitedUserId).HasMaxLength(64);
        e.Property(x => x.PartyExitedReason).HasMaxLength(2000);
        e.Property(x => x.PartyExitedAtUtc);
        e.Property(x => x.IsSocialGroup);
        e.Property(x => x.SocialGroupTitle).HasMaxLength(120);
        e.HasIndex(x => x.OfferId);
        e.HasIndex(x => x.BuyerUserId);
        e.HasIndex(x => x.SellerUserId);
        e.HasIndex(x => new { x.OfferId, x.BuyerUserId })
            .HasFilter("\"DeletedAtUtc\" IS NULL");
        e.HasMany(x => x.Messages)
            .WithOne(x => x.Thread)
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.TradeAgreements)
            .WithOne(x => x.Thread)
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatSocialGroupMemberRowConfiguration : IEntityTypeConfiguration<ChatSocialGroupMemberRow>
{
    public void Configure(EntityTypeBuilder<ChatSocialGroupMemberRow> e)
    {
        e.ToTable("chat_social_group_members");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(48);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.UserId).HasMaxLength(64);
        e.Property(x => x.JoinedAtUtc);
        e.HasIndex(x => x.ThreadId);
        e.HasIndex(x => new { x.ThreadId, x.UserId }).IsUnique();
        e.HasOne<ChatThreadRow>()
            .WithMany()
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatRouteSheetRowConfiguration : IEntityTypeConfiguration<ChatRouteSheetRow>
{
    public void Configure(EntityTypeBuilder<ChatRouteSheetRow> e)
    {
        e.ToTable("chat_route_sheets");
        e.HasKey(x => new { x.ThreadId, x.RouteSheetId });
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.Payload)
            .HasColumnName("PayloadJson")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, RouteSheetJson.Options),
                v => string.IsNullOrWhiteSpace(v)
                    ? new RouteSheetPayload()
                    : JsonSerializer.Deserialize<RouteSheetPayload>(v, RouteSheetJson.Options)
                        ?? new RouteSheetPayload());
        e.Property(x => x.UpdatedAtUtc);
        e.Property(x => x.DeletedAtUtc);
        e.Property(x => x.DeletedByUserId).HasMaxLength(64);
        e.HasIndex(x => x.ThreadId);
        e.HasOne<ChatThreadRow>()
            .WithMany()
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RouteTramoSubscriptionRowConfiguration : IEntityTypeConfiguration<RouteTramoSubscriptionRow>
{
    public void Configure(EntityTypeBuilder<RouteTramoSubscriptionRow> e)
    {
        e.ToTable("route_tramo_subscriptions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(48);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.StopId).HasMaxLength(64);
        e.Property(x => x.CarrierUserId).HasMaxLength(64);
        e.Property(x => x.CarrierPhoneSnapshot).HasMaxLength(40);
        e.Property(x => x.StoreServiceId).HasMaxLength(64);
        e.Property(x => x.TransportServiceLabel).HasMaxLength(512);
        e.Property(x => x.Status).HasMaxLength(24);
        e.Property(x => x.StopContentFingerprint).HasMaxLength(2048);
        e.Property(x => x.CreatedAtUtc);
        e.Property(x => x.UpdatedAtUtc);
        e.HasIndex(x => x.ThreadId);
        e.HasIndex(x => new { x.ThreadId, x.RouteSheetId, x.StopId, x.CarrierUserId });
        e.HasOne<ChatThreadRow>()
            .WithMany()
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmergentOfferRowConfiguration : IEntityTypeConfiguration<EmergentOfferRow>
{
    public void Configure(EntityTypeBuilder<EmergentOfferRow> e)
    {
        e.ToTable("emergent_offers");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.Kind).HasMaxLength(32);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.RouteSheetId).HasMaxLength(64);
        e.Property(x => x.PublisherUserId).HasMaxLength(64);
        e.Property(x => x.RouteSheetSnapshot)
            .HasColumnName("SnapshotJson")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, RouteSheetJson.Options),
                v => string.IsNullOrWhiteSpace(v)
                    ? new EmergentRouteSheetSnapshot()
                    : JsonSerializer.Deserialize<EmergentRouteSheetSnapshot>(v, RouteSheetJson.Options)
                        ?? new EmergentRouteSheetSnapshot());
        e.Property(x => x.PublishedAtUtc);
        e.Property(x => x.RetractedAtUtc);
        e.Property(x => x.OfferQa)
            .HasColumnName("OfferQaJson")
            .HasColumnType("jsonb")
            .HasConversion(OfferQaJson.CreateEfConverter());
        e.HasIndex(x => new { x.ThreadId, x.RouteSheetId }).IsUnique();
        e.HasIndex(x => x.OfferId);
        e.HasIndex(x => new { x.Kind, x.RetractedAtUtc, x.PublishedAtUtc });
    }
}

public sealed class ChatMessageRowConfiguration : IEntityTypeConfiguration<ChatMessageRow>
{
    public void Configure(EntityTypeBuilder<ChatMessageRow> e)
    {
        e.ToTable("chat_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.SenderUserId).HasMaxLength(64);
        e.Property(x => x.Payload)
            .HasColumnName("PayloadJson")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, ChatMessageJson.Options),
                v => ChatMessageJson.DeserializePayload(v));
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.CreatedAtUtc);
        e.Property(x => x.UpdatedAtUtc);
        e.Property(x => x.DeletedAtUtc);
        e.Property(x => x.GroupReceiptsJson);
        e.HasIndex(x => x.ThreadId);
        e.HasIndex(x => new { x.ThreadId, x.CreatedAtUtc });
    }
}

public sealed class ChatNotificationRowConfiguration : IEntityTypeConfiguration<ChatNotificationRow>
{
    public void Configure(EntityTypeBuilder<ChatNotificationRow> e)
    {
        e.ToTable("chat_notifications");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.RecipientUserId).HasMaxLength(64);
        e.Property(x => x.ThreadId).HasMaxLength(64);
        e.Property(x => x.MessageId).HasMaxLength(64);
        e.Property(x => x.OfferId).HasMaxLength(64);
        e.Property(x => x.MessagePreview).HasMaxLength(2000);
        e.Property(x => x.AuthorStoreName).HasMaxLength(512);
        e.Property(x => x.SenderUserId).HasMaxLength(64);
        e.Property(x => x.Kind).HasMaxLength(32);
        e.Property(x => x.MetaJson).HasMaxLength(4000);
        e.Property(x => x.CreatedAtUtc);
        e.Property(x => x.ReadAtUtc);
        e.HasIndex(x => x.RecipientUserId);
        e.HasIndex(x => new { x.RecipientUserId, x.CreatedAtUtc });
        e.HasIndex(x => x.OfferId);
    }
}
