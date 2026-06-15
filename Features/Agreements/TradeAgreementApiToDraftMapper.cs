using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements.Dtos;

namespace VibeTrade.Backend.Features.Agreements;

/// <summary>Convierte respuesta API o entidad persistida en borrador reutilizable (p. ej. duplicar acuerdo).</summary>
public static class TradeAgreementApiToDraftMapper
{
    public static TradeAgreementDraftRequest ToDraftRequest(TradeAgreementRow ag)
    {
        var api = TradeAgreementEntityToApiMapper.ToApiResponse(ag, hasSucceededPayments: false);
        return ToDraftRequest(api);
    }

    public static TradeAgreementDraftRequest ToDraftRequest(TradeAgreementApiResponse api)
    {
        var draft = new TradeAgreementDraftRequest
        {
            Title = api.Title,
            IncludeMerchandise = api.IncludeMerchandise,
            IncludeService = api.IncludeService,
        };

        foreach (var line in api.Merchandise)
        {
            draft.Merchandise.Add(new MerchandiseLineRequest
            {
                LinkedStoreProductId = line.LinkedStoreProductId,
                Tipo = line.Tipo,
                Cantidad = line.Cantidad,
                ValorUnitario = line.ValorUnitario,
                Estado = line.Estado,
                Descuento = line.Descuento,
                Impuestos = line.Impuestos,
                Moneda = line.Moneda,
                TipoEmbalaje = line.TipoEmbalaje,
                DevolucionesDesc = line.DevolucionesDesc,
                DevolucionQuienPaga = line.DevolucionQuienPaga,
                DevolucionPlazos = line.DevolucionPlazos,
                Regulaciones = line.Regulaciones,
            });
        }

        foreach (var s in api.Services)
            draft.Services.Add(MapServiceItem(s));

        foreach (var f in api.ExtraFields)
        {
            draft.ExtraFields ??= new List<TradeAgreementExtraFieldRequest>();
            draft.ExtraFields.Add(new TradeAgreementExtraFieldRequest
            {
                Id = f.Id,
                Title = f.Title,
                ValueKind = f.ValueKind,
                TextValue = f.TextValue,
                MediaUrl = f.MediaUrl,
                FileName = f.FileName,
            });
        }

        return draft;
    }

    private static ServiceItemRequest MapServiceItem(ServiceItemApi s)
    {
        var item = new ServiceItemRequest
        {
            LinkedStoreServiceId = s.LinkedStoreServiceId,
            Configured = s.Configured,
            TipoServicio = s.TipoServicio,
            Tiempo = new TiempoRangeRequest
            {
                StartDate = s.Tiempo.StartDate,
                EndDate = s.Tiempo.EndDate,
            },
            Horarios = new HorariosRequest
            {
                Months = s.Horarios.Months?.ToList(),
                CalendarYear = s.Horarios.CalendarYear,
                DaysByMonth = s.Horarios.DaysByMonth is null
                    ? null
                    : new Dictionary<string, List<int>>(s.Horarios.DaysByMonth),
                DefaultWindow = s.Horarios.DefaultWindow is null
                    ? null
                    : new TimeWindowRequest
                    {
                        Start = s.Horarios.DefaultWindow.Start,
                        End = s.Horarios.DefaultWindow.End,
                    },
                DayHourOverrides = s.Horarios.DayHourOverrides is null
                    ? null
                    : s.Horarios.DayHourOverrides.ToDictionary(
                        kv => kv.Key,
                        kv => new TimeWindowRequest { Start = kv.Value.Start, End = kv.Value.End }),
            },
            RecurrenciaPagos = new RecurrenciaPagosRequest
            {
                Months = s.RecurrenciaPagos.Months?.ToList(),
                Entries = s.RecurrenciaPagos.Entries?
                    .Select(e => new PaymentEntryRequest
                    {
                        Month = e.Month,
                        Day = e.Day,
                        Amount = e.Amount,
                        Moneda = e.Moneda,
                    })
                    .ToList(),
            },
            Descripcion = s.Descripcion,
            Riesgos = new RiesgosBlockRequest
            {
                Enabled = s.Riesgos.Enabled,
                Items = s.Riesgos.Items?.ToList(),
            },
            Incluye = s.Incluye,
            NoIncluye = s.NoIncluye,
            Dependencias = new DependenciasBlockRequest
            {
                Enabled = s.Dependencias.Enabled,
                Items = s.Dependencias.Items?.ToList(),
            },
            Entregables = s.Entregables,
            Garantias = new GarantiasBlockRequest
            {
                Enabled = s.Garantias.Enabled,
                Texto = s.Garantias.Texto,
            },
            PenalAtraso = new PenalAtrasoBlockRequest
            {
                Enabled = s.PenalAtraso.Enabled,
                Texto = s.PenalAtraso.Texto,
            },
            Terminacion = new TerminacionBlockRequest
            {
                Enabled = s.Terminacion.Enabled,
                Causas = s.Terminacion.Causas?.ToList(),
                AvisoDias = s.Terminacion.AvisoDias,
            },
            MetodoPago = s.MetodoPago,
            MonedasAceptadas = s.MonedasAceptadas?.ToList(),
            Moneda = s.Moneda,
            MedicionCumplimiento = s.MedicionCumplimiento,
            PenalIncumplimiento = s.PenalIncumplimiento,
            NivelResponsabilidad = s.NivelResponsabilidad,
            PropIntelectual = s.PropIntelectual,
        };

        if (s.CondicionesExtras is { Count: > 0 })
        {
            item.CondicionesExtras = s.CondicionesExtras
                .Select(f => new TradeAgreementExtraFieldRequest
                {
                    Id = f.Id,
                    Title = f.Title,
                    ValueKind = f.ValueKind,
                    TextValue = f.TextValue,
                    MediaUrl = f.MediaUrl,
                    FileName = f.FileName,
                })
                .ToList();
        }

        return item;
    }
}
