using Microsoft.AspNetCore.Http;

namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <summary>Construye cuerpos de error HTTP coherentes con el registro de políticas.</summary>
public static class ChatExitResponseFactory
{
    private const string PartyLeaveGenericMessage =
        "No se pudo completar la salida. Si eres transportista, usa Salir desde la lista del chat";

    public static (int StatusCode, object Body) PartySoftLeaveFailure(
        string? errorCode,
        IChatExitPolicyRegistry registry)
    {
        if (registry.TryMapPartySoftLeaveFailure(errorCode, out var st, out var msg))
            return (st, new { error = errorCode, message = msg });

        return (
            StatusCodes.Status400BadRequest,
            new
            {
                error = errorCode ?? "party_leave_failed",
                message = PartyLeaveGenericMessage,
            });
    }

}
