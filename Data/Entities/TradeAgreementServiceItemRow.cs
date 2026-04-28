namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementServiceItemRow
{
    public string Id { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public int SortOrder { get; set; }

    public string? LinkedStoreServiceId { get; set; }

    public bool Configured { get; set; }

    public string TipoServicio { get; set; } = "";

    public string TiempoStartDate { get; set; } = "";

    public string TiempoEndDate { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public string Incluye { get; set; } = "";

    public string NoIncluye { get; set; } = "";

    public string Entregables { get; set; } = "";

    public string MetodoPago { get; set; } = "";

    public string Moneda { get; set; } = "";

    public string MedicionCumplimiento { get; set; } = "";

    public string PenalIncumplimiento { get; set; } = "";

    public string NivelResponsabilidad { get; set; } = "";

    public string PropIntelectual { get; set; } = "";

    /// <summary>JSON: cláusulas libres configuradas en el asistente (paso Condiciones comerciales).</summary>
    public string? CondicionesExtrasJson { get; set; }

    public int ScheduleCalendarYear { get; set; }

    public string ScheduleDefaultWindowStart { get; set; } = "";

    public string ScheduleDefaultWindowEnd { get; set; } = "";

    public bool RiesgosEnabled { get; set; }

    public bool DependenciasEnabled { get; set; }

    public bool GarantiasEnabled { get; set; }

    public string GarantiasTexto { get; set; } = "";

    public bool PenalAtrasoEnabled { get; set; }

    public string PenalAtrasoTexto { get; set; } = "";

    public bool TerminacionEnabled { get; set; }

    public string TerminacionAvisoDias { get; set; } = "";

    public ICollection<TradeAgreementServiceScheduleMonthRow> ScheduleMonths { get; set; } =
        new List<TradeAgreementServiceScheduleMonthRow>();

    public ICollection<TradeAgreementServiceScheduleDayRow> ScheduleDays { get; set; } =
        new List<TradeAgreementServiceScheduleDayRow>();

    public ICollection<TradeAgreementServiceScheduleOverrideRow> ScheduleOverrides { get; set; } =
        new List<TradeAgreementServiceScheduleOverrideRow>();

    public ICollection<TradeAgreementServicePaymentMonthRow> PaymentMonths { get; set; } =
        new List<TradeAgreementServicePaymentMonthRow>();

    public ICollection<TradeAgreementServicePaymentEntryRow> PaymentEntries { get; set; } =
        new List<TradeAgreementServicePaymentEntryRow>();

    public ICollection<TradeAgreementServiceRiesgoRow> RiesgoItems { get; set; } =
        new List<TradeAgreementServiceRiesgoRow>();

    public ICollection<TradeAgreementServiceDependenciaRow> DependenciaItems { get; set; } =
        new List<TradeAgreementServiceDependenciaRow>();

    public ICollection<TradeAgreementServiceTerminacionCausaRow> TerminacionCausas { get; set; } =
        new List<TradeAgreementServiceTerminacionCausaRow>();

    public ICollection<TradeAgreementServiceMonedaRow> MonedasAceptadas { get; set; } =
        new List<TradeAgreementServiceMonedaRow>();
}
