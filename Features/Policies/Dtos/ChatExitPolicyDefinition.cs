namespace VibeTrade.Backend.Features.Policies.Dtos;

/// <summary>Fila del registro de políticas de bloqueo al salir del chat.</summary>
public sealed record ChatExitPolicyDefinition(
    string Code,
    string Audience,
    int HttpStatus,
    string MessageEs,
    string SummaryEs);
