namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoWithdrawSystemText
{
    public static string BuildAutomatedNotice(string whoDisplay, int nTramos, int nSheets, bool applyTrustPenalty)
    {
        var who = (whoDisplay ?? "").Trim();
        if (who.Length == 0)
            who = "Un transportista";
        if (who.Length > 120)
            who = who[..120] + "…";
        var sys = nSheets <= 1
            ? $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s))."
            : $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s) en {nSheets} hojas de ruta).";
        if (applyTrustPenalty)
            sys += " Se aplicó un ajuste de confianza por abandono de ruta antes de la entrega.";
        return sys;
    }
}
