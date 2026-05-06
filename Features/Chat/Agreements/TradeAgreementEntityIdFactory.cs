namespace VibeTrade.Backend.Features.Chat.Agreements;

internal static class TradeAgreementEntityIdFactory
{
    internal static string NewId(string prefix)
    {
        var s = $"{prefix}_{Guid.NewGuid():N}";
        return s.Length <= 64 ? s : s[..64];
    }

    internal static string TrimId(string id, int max) =>
        id.Length <= max ? id : id[..max];
}
