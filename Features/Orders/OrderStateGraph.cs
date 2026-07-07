namespace VibeTrade.Backend.Features.Orders;

/// <summary>
/// Grafo de estados del pedido: <c>procesado → en_transito → entregado</c>.
/// «Entregado» solo se alcanza cuando el comprador acepta la evidencia tienda → cliente
/// (que a su vez está condicionada a que la logística resuelva todos los tramos).
/// </summary>
public static class OrderStateGraph
{
    public static IReadOnlyList<string> NextStates(string state)
    {
        var s = (state ?? "").Trim();
        return s switch
        {
            OrderStatuses.Procesado => [OrderStatuses.EnTransito],
            OrderStatuses.EnTransito => [OrderStatuses.Entregado],
            OrderStatuses.Entregado => [],
            _ => [],
        };
    }

    public static bool CanTransition(string from, string to) =>
        NextStates(from).Contains((to ?? "").Trim(), StringComparer.Ordinal);
}
