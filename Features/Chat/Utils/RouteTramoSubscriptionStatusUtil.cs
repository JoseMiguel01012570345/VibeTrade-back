using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoSubscriptionStatusUtil
{
    public static string Normalized(string? status) => (status ?? "pending").Trim().ToLowerInvariant();

    /// <summary>Pendiente de decisión del vendedor (no confirmado / rechazado / retirado).</summary>
    public static bool IsPendingForSellerDecision(RouteTramoSubscriptionRow r) =>
        Normalized(r.Status) is not ("confirmed" or "rejected" or "withdrawn");
}
