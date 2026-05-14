using System.Text.Json;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Agreements;

public static class TradeAgreementEntityToApiMapper
{
    private static readonly JsonSerializerOptions CondicionesExtrasReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    public static TradeAgreementApiResponse ToApiResponse(
        TradeAgreementRow ag,
        bool hasSucceededPayments = false)
    {
        var resp = MapAgreementHeader(ag, hasSucceededPayments);
        MapMerchandiseMeta(ag, resp);
        MapMerchandiseLines(ag, resp);
        MapServiceItems(ag, resp);
        MapExtraFields(ag, resp);
        return resp;
    }

    private static TradeAgreementApiResponse MapAgreementHeader(
        TradeAgreementRow ag,
        bool hasSucceededPayments)
    {
        var deleted = ag.DeletedAtUtc is not null;
        return new TradeAgreementApiResponse
        {
            Id = ag.Id,
            ThreadId = ag.ThreadId,
            Title = ag.Title,
            IssuedAt = ag.IssuedAtUtc.ToUnixTimeMilliseconds(),
            IssuedByStoreId = ag.IssuedByStoreId,
            IssuerLabel = ag.IssuerLabel,
            Status = deleted ? "deleted" : ag.Status,
            DeletedAt = deleted ? ag.DeletedAtUtc!.Value.ToUnixTimeMilliseconds() : null,
            RespondedAt = ag.RespondedAtUtc?.ToUnixTimeMilliseconds(),
            SellerEditBlockedUntilBuyerResponse = ag.SellerEditBlockedUntilBuyerResponse ? true : null,
            HadBuyerAcceptance = ag.HadBuyerAcceptance ? true : null,
            IncludeMerchandise = ag.IncludeMerchandise,
            IncludeService = ag.IncludeService,
            RouteSheetId = ag.RouteSheetId,
            RouteSheetUrl = ag.RouteSheetUrl,
            HasSucceededPayments = hasSucceededPayments,
        };
    }

    private static void MapMerchandiseMeta(TradeAgreementRow ag, TradeAgreementApiResponse resp)
    {
        if (ag.MerchandiseMeta is not { } meta)
            return;
        resp.MerchandiseMeta = new MerchandiseSectionMetaApi
        {
            Moneda = meta.Moneda,
            TipoEmbalaje = meta.TipoEmbalaje,
            DevolucionesDesc = meta.DevolucionesDesc,
            DevolucionQuienPaga = meta.DevolucionQuienPaga,
            DevolucionPlazos = meta.DevolucionPlazos,
            Regulaciones = meta.Regulaciones,
        };
    }

    private static void MapMerchandiseLines(TradeAgreementRow ag, TradeAgreementApiResponse resp)
    {
        foreach (var line in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            resp.Merchandise.Add(new MerchandiseLineApi
            {
                Id = line.Id.Trim(),
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
    }

    private static void MapServiceItems(TradeAgreementRow ag, TradeAgreementApiResponse resp)
    {
        foreach (var s in ag.ServiceItems.OrderBy(x => x.SortOrder))
            resp.Services.Add(MapServiceItem(s));
    }

    private static void MapExtraFields(TradeAgreementRow ag, TradeAgreementApiResponse resp)
    {
        foreach (var f in ag.ExtraFields.OrderBy(x => x.SortOrder))
        {
            resp.ExtraFields.Add(new TradeAgreementExtraFieldApi
            {
                Id = f.Id,
                Title = f.Title,
                ValueKind = f.ValueKind,
                TextValue = f.TextValue,
                MediaUrl = f.MediaUrl,
                FileName = f.FileName,
            });
        }
    }

    private static ServiceItemApi MapServiceItem(TradeAgreementServiceItemRow s)
    {
        return new ServiceItemApi
        {
            Id = s.Id,
            LinkedStoreServiceId = s.LinkedStoreServiceId,
            Configured = s.Configured,
            TipoServicio = s.TipoServicio,
            Tiempo = new TiempoApi { StartDate = s.TiempoStartDate, EndDate = s.TiempoEndDate },
            Horarios = MapHorarios(s),
            RecurrenciaPagos = MapRecurrenciaPagos(s),
            Descripcion = s.Descripcion,
            Riesgos = new RiesgosApi
            {
                Enabled = s.RiesgosEnabled,
                Items = s.RiesgoItems.OrderBy(x => x.SortOrder).Select(x => x.Text).ToList(),
            },
            Incluye = s.Incluye,
            NoIncluye = s.NoIncluye,
            Dependencias = new DependenciasApi
            {
                Enabled = s.DependenciasEnabled,
                Items = s.DependenciaItems.OrderBy(x => x.SortOrder).Select(x => x.Text).ToList(),
            },
            Entregables = s.Entregables,
            Garantias = new GarantiasApi { Enabled = s.GarantiasEnabled, Texto = s.GarantiasTexto },
            PenalAtraso = new PenalAtrasoApi
                { Enabled = s.PenalAtrasoEnabled, Texto = s.PenalAtrasoTexto },
            Terminacion = new TerminacionApi
            {
                Enabled = s.TerminacionEnabled,
                Causas = s.TerminacionCausas.OrderBy(x => x.SortOrder).Select(x => x.Text).ToList(),
                AvisoDias = s.TerminacionAvisoDias,
            },
            MetodoPago = s.MetodoPago,
            MonedasAceptadas = s.MonedasAceptadas.OrderBy(x => x.SortOrder).Select(x => x.Code).ToList(),
            Moneda = s.Moneda,
            MedicionCumplimiento = s.MedicionCumplimiento,
            PenalIncumplimiento = s.PenalIncumplimiento,
            NivelResponsabilidad = s.NivelResponsabilidad,
            PropIntelectual = s.PropIntelectual,
            CondicionesExtras = DeserializeCondicionesExtrasApi(s.CondicionesExtrasJson),
        };
    }

    private static List<TradeAgreementExtraFieldApi> DeserializeCondicionesExtrasApi(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<TradeAgreementExtraFieldApi>>(raw.Trim(),
                CondicionesExtrasReadOpts);
            return list ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static HorariosApi MapHorarios(TradeAgreementServiceItemRow s)
    {
        var daysByMonth = new Dictionary<string, List<int>>();
        foreach (var g in s.ScheduleDays.GroupBy(x => x.Month).OrderBy(x => x.Key))
            daysByMonth[g.Key.ToString()] = g.Select(x => x.CalendarDay).Order().ToList();

        var overrides = new Dictionary<string, TimeWindowApi>();
        foreach (var o in s.ScheduleOverrides)
            overrides[$"{o.Month}-{o.CalendarDay}"] = new TimeWindowApi
                { Start = o.WindowStart, End = o.WindowEnd };

        return new HorariosApi
        {
            Months = s.ScheduleMonths.Select(x => x.Month).Order().ToList(),
            CalendarYear = s.ScheduleCalendarYear,
            DaysByMonth = daysByMonth,
            DefaultWindow = new TimeWindowApi
            {
                Start = s.ScheduleDefaultWindowStart,
                End = s.ScheduleDefaultWindowEnd,
            },
            DayHourOverrides = overrides,
        };
    }

    private static RecurrenciaPagosApi MapRecurrenciaPagos(TradeAgreementServiceItemRow s)
    {
        return new RecurrenciaPagosApi
        {
            Months = s.PaymentMonths.Select(x => x.Month).Order().ToList(),
            Entries = s.PaymentEntries.OrderBy(x => x.SortOrder).Select(e => new PaymentEntryApi
            {
                Month = e.Month,
                Day = e.Day,
                Amount = e.Amount,
                Moneda = e.Moneda,
            }).ToList(),
        };
    }
}
