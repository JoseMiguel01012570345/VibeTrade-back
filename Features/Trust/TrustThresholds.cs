namespace VibeTrade.Backend.Features.Trust;

/// <summary>
/// Umbral de confianza y estados de la barra (wiki cap. 08/10). Por debajo del umbral las
/// interacciones se bloquean y solo se habilita el pago de la mensualidad; al cruzar el umbral
/// hacia arriba se rehabilitan.
/// </summary>
public static class TrustThresholds
{
    /// <summary>Puntaje mínimo para mantener las interacciones habilitadas (estado «Activa»).</summary>
    public const int InteractionThreshold = 20;

    public static bool IsBlocked(int score) => score < InteractionThreshold;

    public static string StateFor(int score) => IsBlocked(score) ? TrustGateStates.Blocked : TrustGateStates.Active;
}

/// <summary>Estados de la barra de confianza expuestos a la UI.</summary>
public static class TrustGateStates
{
    public const string Active = "active";
    public const string Blocked = "blocked";
}
