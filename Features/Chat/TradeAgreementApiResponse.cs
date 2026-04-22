namespace VibeTrade.Backend.Features.Chat;

/// <summary>Respuesta alineada a <c>TradeAgreement</c> en el cliente.</summary>
public sealed class TradeAgreementApiResponse
{
    public string Id { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string Title { get; set; } = "";
    public long IssuedAt { get; set; }
    public string IssuedByStoreId { get; set; } = "";
    public string IssuerLabel { get; set; } = "";
    public string Status { get; set; } = "";
    public long? RespondedAt { get; set; }
    public bool? SellerEditBlockedUntilBuyerResponse { get; set; }
    public bool IncludeMerchandise { get; set; }
    public bool IncludeService { get; set; }
    public List<MerchandiseLineApi> Merchandise { get; set; } = new();
    public MerchandiseSectionMetaApi? MerchandiseMeta { get; set; }
    public List<ServiceItemApi> Services { get; set; } = new();
    public string? RouteSheetId { get; set; }
    public string? RouteSheetUrl { get; set; }
}

public sealed class MerchandiseLineApi
{
    public string? LinkedStoreProductId { get; set; }
    public string Tipo { get; set; } = "";
    public string Cantidad { get; set; } = "";
    public string ValorUnitario { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Descuento { get; set; } = "";
    public string Impuestos { get; set; } = "";
    public string Moneda { get; set; } = "";
    public string TipoEmbalaje { get; set; } = "";
    public string DevolucionesDesc { get; set; } = "";
    public string DevolucionQuienPaga { get; set; } = "";
    public string DevolucionPlazos { get; set; } = "";
    public string Regulaciones { get; set; } = "";
}

public sealed class MerchandiseSectionMetaApi
{
    public string Moneda { get; set; } = "";
    public string TipoEmbalaje { get; set; } = "";
    public string DevolucionesDesc { get; set; } = "";
    public string DevolucionQuienPaga { get; set; } = "";
    public string DevolucionPlazos { get; set; } = "";
    public string Regulaciones { get; set; } = "";
}

public sealed class ServiceItemApi
{
    public string Id { get; set; } = "";
    public string? LinkedStoreServiceId { get; set; }
    public bool Configured { get; set; }
    public string TipoServicio { get; set; } = "";
    public TiempoApi Tiempo { get; set; } = new();
    public HorariosApi Horarios { get; set; } = new();
    public RecurrenciaPagosApi RecurrenciaPagos { get; set; } = new();
    public string Descripcion { get; set; } = "";
    public RiesgosApi Riesgos { get; set; } = new();
    public string Incluye { get; set; } = "";
    public string NoIncluye { get; set; } = "";
    public DependenciasApi Dependencias { get; set; } = new();
    public string Entregables { get; set; } = "";
    public GarantiasApi Garantias { get; set; } = new();
    public PenalAtrasoApi PenalAtraso { get; set; } = new();
    public TerminacionApi Terminacion { get; set; } = new();
    public string MetodoPago { get; set; } = "";
    public List<string>? MonedasAceptadas { get; set; }
    public string Moneda { get; set; } = "";
    public string MedicionCumplimiento { get; set; } = "";
    public string PenalIncumplimiento { get; set; } = "";
    public string NivelResponsabilidad { get; set; } = "";
    public string PropIntelectual { get; set; } = "";
}

public sealed class TiempoApi
{
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
}

public sealed class HorariosApi
{
    public List<int> Months { get; set; } = new();
    public int CalendarYear { get; set; }
    public Dictionary<string, List<int>> DaysByMonth { get; set; } = new();
    public TimeWindowApi DefaultWindow { get; set; } = new();
    public Dictionary<string, TimeWindowApi> DayHourOverrides { get; set; } = new();
}

public sealed class TimeWindowApi
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public sealed class RecurrenciaPagosApi
{
    public List<int> Months { get; set; } = new();
    public List<PaymentEntryApi> Entries { get; set; } = new();
}

public sealed class PaymentEntryApi
{
    public int Month { get; set; }
    public int Day { get; set; }
    public string Amount { get; set; } = "";
}

public sealed class RiesgosApi
{
    public bool Enabled { get; set; }
    public List<string> Items { get; set; } = new();
}

public sealed class DependenciasApi
{
    public bool Enabled { get; set; }
    public List<string> Items { get; set; } = new();
}

public sealed class GarantiasApi
{
    public bool Enabled { get; set; }
    public string Texto { get; set; } = "";
}

public sealed class PenalAtrasoApi
{
    public bool Enabled { get; set; }
    public string Texto { get; set; } = "";
}

public sealed class TerminacionApi
{
    public bool Enabled { get; set; }
    public List<string> Causas { get; set; } = new();
    public string AvisoDias { get; set; } = "";
}
