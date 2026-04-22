using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Body JSON alineado al borrador del cliente (<c>TradeAgreementDraft</c>).</summary>
public sealed class TradeAgreementDraftRequest
{
    public string Title { get; set; } = "";

    [JsonPropertyName("includeMerchandise")]
    public bool IncludeMerchandise { get; set; } = true;

    [JsonPropertyName("includeService")]
    public bool IncludeService { get; set; } = true;

    public List<MerchandiseLineRequest> Merchandise { get; set; } = new();

    public List<ServiceItemRequest> Services { get; set; } = new();
}

public sealed class MerchandiseLineRequest
{
    [JsonPropertyName("linkedStoreProductId")]
    public string? LinkedStoreProductId { get; set; }

    public string Tipo { get; set; } = "";
    public string Cantidad { get; set; } = "";
    public string ValorUnitario { get; set; } = "";
    public string Estado { get; set; } = "nuevo";
    public string Descuento { get; set; } = "";
    public string Impuestos { get; set; } = "";
    public string Moneda { get; set; } = "";
    public string TipoEmbalaje { get; set; } = "";

    [JsonPropertyName("devolucionesDesc")]
    public string DevolucionesDesc { get; set; } = "";

    [JsonPropertyName("devolucionQuienPaga")]
    public string DevolucionQuienPaga { get; set; } = "";

    [JsonPropertyName("devolucionPlazos")]
    public string DevolucionPlazos { get; set; } = "";

    public string Regulaciones { get; set; } = "";
}

public sealed class ServiceItemRequest
{
    public string? Id { get; set; }

    [JsonPropertyName("linkedStoreServiceId")]
    public string? LinkedStoreServiceId { get; set; }

    public bool Configured { get; set; }
    public string TipoServicio { get; set; } = "";
    public TiempoRangeRequest Tiempo { get; set; } = new();
    public HorariosRequest Horarios { get; set; } = new();

    [JsonPropertyName("recurrenciaPagos")]
    public RecurrenciaPagosRequest RecurrenciaPagos { get; set; } = new();

    public string Descripcion { get; set; } = "";
    public RiesgosBlockRequest Riesgos { get; set; } = new();
    public string Incluye { get; set; } = "";
    public string NoIncluye { get; set; } = "";
    public DependenciasBlockRequest Dependencias { get; set; } = new();
    public string Entregables { get; set; } = "";
    public GarantiasBlockRequest Garantias { get; set; } = new();
    public PenalAtrasoBlockRequest PenalAtraso { get; set; } = new();
    public TerminacionBlockRequest Terminacion { get; set; } = new();

    [JsonPropertyName("metodoPago")]
    public string MetodoPago { get; set; } = "";

    [JsonPropertyName("monedasAceptadas")]
    public List<string>? MonedasAceptadas { get; set; }

    public string Moneda { get; set; } = "";

    [JsonPropertyName("medicionCumplimiento")]
    public string MedicionCumplimiento { get; set; } = "";

    [JsonPropertyName("penalIncumplimiento")]
    public string PenalIncumplimiento { get; set; } = "";

    [JsonPropertyName("nivelResponsabilidad")]
    public string NivelResponsabilidad { get; set; } = "";

    [JsonPropertyName("propIntelectual")]
    public string PropIntelectual { get; set; } = "";
}

public sealed class TiempoRangeRequest
{
    [JsonPropertyName("startDate")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("endDate")]
    public string EndDate { get; set; } = "";
}

public sealed class HorariosRequest
{
    public List<int>? Months { get; set; }

    [JsonPropertyName("calendarYear")]
    public int CalendarYear { get; set; }

    [JsonPropertyName("daysByMonth")]
    public Dictionary<string, List<int>>? DaysByMonth { get; set; }

    [JsonPropertyName("defaultWindow")]
    public TimeWindowRequest? DefaultWindow { get; set; }

    [JsonPropertyName("dayHourOverrides")]
    public Dictionary<string, TimeWindowRequest>? DayHourOverrides { get; set; }
}

public sealed class TimeWindowRequest
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public sealed class RecurrenciaPagosRequest
{
    public List<int>? Months { get; set; }
    public List<PaymentEntryRequest>? Entries { get; set; }
}

public sealed class PaymentEntryRequest
{
    public int Month { get; set; }
    public int Day { get; set; }
    public string Amount { get; set; } = "";

    [JsonPropertyName("moneda")]
    public string Moneda { get; set; } = "";
}

public sealed class RiesgosBlockRequest
{
    public bool Enabled { get; set; }
    public List<string>? Items { get; set; }
}

public sealed class DependenciasBlockRequest
{
    public bool Enabled { get; set; }
    public List<string>? Items { get; set; }
}

public sealed class GarantiasBlockRequest
{
    public bool Enabled { get; set; }
    public string Texto { get; set; } = "";
}

public sealed class PenalAtrasoBlockRequest
{
    public bool Enabled { get; set; }
    public string Texto { get; set; } = "";
}

public sealed class TerminacionBlockRequest
{
    public bool Enabled { get; set; }
    public List<string>? Causas { get; set; }

    [JsonPropertyName("avisoDias")]
    public string AvisoDias { get; set; } = "";
}
