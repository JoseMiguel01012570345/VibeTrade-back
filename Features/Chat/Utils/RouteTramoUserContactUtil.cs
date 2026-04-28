using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoUserContactUtil
{
    public static string BestPhoneForCarrier(UserAccount? acc, string? subSnapshot, RouteStopPayload? parada)
    {
        var phone = (acc?.PhoneDisplay ?? "").Trim();
        if (phone.Length == 0 && !string.IsNullOrWhiteSpace(acc?.PhoneDigits))
            phone = acc!.PhoneDigits!.Trim();
        if (phone.Length == 0)
            phone = (subSnapshot ?? "").Trim();
        if (phone.Length == 0 && parada is not null)
            phone = (parada.TelefonoTransportista ?? "").Trim();
        return phone;
    }

    public static string CarrierDisplayOrDefault(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Transportista" : displayName.Trim();

    public static string ParticipanteOrDisplay(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Participante" : displayName!.Trim();
}
