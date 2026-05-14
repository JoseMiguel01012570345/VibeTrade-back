namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <summary>Registro inmutable de políticas de salida: mapeo de códigos de error a HTTP y mensajes.</summary>
public interface IChatExitPolicyRegistry
{
    bool TryMapPartySoftLeaveFailure(string? errorCode, out int statusCode, out string message);

    bool TryMapCarrierWithdrawFailure(string? errorCode, out int statusCode, out string message);
}
