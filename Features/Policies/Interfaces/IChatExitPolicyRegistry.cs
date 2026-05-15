namespace VibeTrade.Backend.Features.Policies.Interfaces;

/// <summary>Registro inmutable de políticas de salida: mapeo de códigos de error a HTTP, mensajes y cuerpo de error.</summary>
public interface IChatExitPolicyRegistry
{
    bool TryMapPartySoftLeaveFailure(string? errorCode, out int statusCode, out string message);

    bool TryMapCarrierWithdrawFailure(string? errorCode, out int statusCode, out string message);

    /// <summary>Código HTTP y cuerpo JSON para fallo de party-soft-leave.</summary>
    (int StatusCode, object Body) PartySoftLeaveFailure(string? errorCode);
}
