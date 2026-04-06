namespace VibeTrade.Backend.Utils.TimeZone;

/// <summary>Timezone del cliente (header X-Timezone, flow-ui notas técnicas).</summary>
public sealed class RequestTimeZoneContext
{
    public string? TimeZoneId { get; set; }
}
