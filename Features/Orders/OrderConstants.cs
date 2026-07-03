namespace VibeTrade.Backend.Features.Orders;

/// <summary>Estados del pedido de mercancía (Pedido), alineados con el wiki (cap. 04).</summary>
public static class OrderStatuses
{
    /// <summary>Pedido creado y pago retenido; aún no sale del almacén.</summary>
    public const string Procesado = "procesado";

    /// <summary>La mercancía salió del almacén y está en la hoja de ruta.</summary>
    public const string EnTransito = "en_transito";

    /// <summary>Entregado al cliente (evidencia aceptada) y pago liberado.</summary>
    public const string Entregado = "entregado";

    public static bool IsKnown(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s is Procesado or EnTransito or Entregado;
    }
}

/// <summary>Modalidad de entrega elegida en el checkout.</summary>
public static class OrderDeliveryModes
{
    /// <summary>Recogida por el cliente en el almacén (sin tarifa de mensajería).</summary>
    public const string Pickup = "pickup";

    /// <summary>Envío a domicilio vía mensajería (tarifa por distancia).</summary>
    public const string Shipping = "shipping";

    public static bool IsKnown(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s is Pickup or Shipping;
    }
}

/// <summary>Estado del pago retenido del pedido.</summary>
public static class OrderPaymentStatuses
{
    /// <summary>Cobrado y retenido por la plataforma (garantía).</summary>
    public const string Held = "held";

    /// <summary>Liberado al vendedor tras aceptación de la evidencia.</summary>
    public const string Released = "released";

    /// <summary>Reembolsado al comprador (invalidación / expulsión).</summary>
    public const string Refunded = "refunded";
}

/// <summary>Decisión del comprador sobre la evidencia de entrega tienda → cliente.</summary>
public static class OrderClientEvidenceDecisions
{
    /// <summary>La tienda aún no subió evidencia.</summary>
    public const string None = "none";

    /// <summary>Evidencia subida, pendiente de que el comprador acepte o rechace.</summary>
    public const string Pending = "pending";

    public const string Accepted = "accepted";

    public const string Rejected = "rejected";
}
