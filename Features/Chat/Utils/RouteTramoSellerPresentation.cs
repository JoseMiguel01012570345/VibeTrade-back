using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoSellerPresentation
{
    public static (string Label, int Trust) LabelAndTrust(StoreRow? store, UserAccount? actorAcc)
    {
        var label = !string.IsNullOrWhiteSpace(store?.Name) ? store!.Name.Trim()
            : string.IsNullOrWhiteSpace(actorAcc?.DisplayName) ? "Vendedor"
            : actorAcc!.DisplayName.Trim();
        var trust = store?.TrustScore ?? actorAcc?.TrustScore ?? 0;
        return (label, trust);
    }
}
