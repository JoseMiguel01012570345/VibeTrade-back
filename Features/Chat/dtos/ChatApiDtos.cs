using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Chat.Dtos;

public sealed record CreateThreadBody(string OfferId, bool? PurchaseIntent, bool? ForceNew);

public sealed record CreateSocialGroupBody(IReadOnlyList<string>? MemberUserIds);

public sealed record AckPendingDeliveryOnLoginResult(int Applied);

public sealed record PatchSocialGroupTitleBody(string? Title);

public sealed record UpdateMessageStatusBody(string Status);

public sealed record AcceptRouteTramoSubscriptionBody(string RouteSheetId, string CarrierUserId, string? StopId = null);

public sealed record RejectRouteTramoSubscriptionBody(string RouteSheetId, string CarrierUserId, string? StopId = null);

public sealed record SellerExpelCarrierBody(
    string CarrierUserId,
    string Reason,
    string? RouteSheetId = null,
    string? StopId = null);

public sealed record NotifyPreselectedBody(RouteSheetPreselectedInvite[]? Invites);

public sealed record NotifyPreselectedResult(int NotifiedCount);

public sealed record CarrierPreselInviteBody(string RouteSheetId, string? StopId, bool Accepted);

public sealed record RouteSheetEditCarrierResponseBody(bool Accept);

public sealed record MarkReadBody(string[]? Ids);

public sealed record LinkPreviewResponse(string Url, string? Title, string? Description, string? ImageUrl);
