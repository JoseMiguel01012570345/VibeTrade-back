using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat;

public static class TradeAgreementDraftToEntityMapper
{
    public static void ReplaceContentFromDraft(TradeAgreementRow ag, TradeAgreementDraftRequest draft)
    {
        ag.MerchandiseLines.Clear();
        ag.MerchandiseMeta = null;
        ag.ServiceItems.Clear();

        ag.IncludeMerchandise = draft.IncludeMerchandise;
        ag.IncludeService = draft.IncludeService;

        if (draft.IncludeMerchandise)
            AddMerchandiseLines(ag, draft);

        if (!draft.IncludeService)
            return;

        AddServiceItems(ag, draft);
    }

    private static void AddMerchandiseLines(TradeAgreementRow ag, TradeAgreementDraftRequest draft)
    {
        var order = 0;
        foreach (var line in draft.Merchandise)
        {
            ag.MerchandiseLines.Add(new TradeAgreementMerchandiseLineRow
            {
                Id = TradeAgreementEntityIdFactory.NewId("aml"),
                TradeAgreementId = ag.Id,
                SortOrder = order++,
                LinkedStoreProductId = string.IsNullOrWhiteSpace(line.LinkedStoreProductId)
                    ? null
                    : line.LinkedStoreProductId.Trim(),
                Tipo = line.Tipo ?? "",
                Cantidad = line.Cantidad ?? "",
                ValorUnitario = line.ValorUnitario ?? "",
                Estado = string.IsNullOrWhiteSpace(line.Estado) ? "nuevo" : line.Estado.Trim(),
                Descuento = line.Descuento ?? "",
                Impuestos = line.Impuestos ?? "",
                Moneda = line.Moneda ?? "",
                TipoEmbalaje = line.TipoEmbalaje ?? "",
                DevolucionesDesc = line.DevolucionesDesc ?? "",
                DevolucionQuienPaga = line.DevolucionQuienPaga ?? "",
                DevolucionPlazos = line.DevolucionPlazos ?? "",
                Regulaciones = line.Regulaciones ?? "",
            });
        }
    }

    private static void AddServiceItems(TradeAgreementRow ag, TradeAgreementDraftRequest draft)
    {
        var sortOrder = 0;
        foreach (var s in draft.Services)
        {
            var serviceItemId = string.IsNullOrWhiteSpace(s.Id)
                ? TradeAgreementEntityIdFactory.NewId("svi")
                : TradeAgreementEntityIdFactory.TrimId(s.Id!, 80);

            var row = CreateServiceItemRow(ag.Id, serviceItemId, sortOrder++, s);
            AppendScheduleCollections(row, serviceItemId, s);
            AppendPaymentCollections(row, serviceItemId, s);
            AppendRiesgos(row, serviceItemId, s);
            AppendDependencias(row, serviceItemId, s);
            AppendTerminacionCausas(row, serviceItemId, s);
            AppendMonedasAceptadas(row, serviceItemId, s);

            ag.ServiceItems.Add(row);
        }
    }

    private static TradeAgreementServiceItemRow CreateServiceItemRow(
        string agreementId,
        string serviceItemId,
        int sortOrder,
        ServiceItemRequest s)
    {
        return new TradeAgreementServiceItemRow
        {
            Id = serviceItemId,
            TradeAgreementId = agreementId,
            SortOrder = sortOrder,
            LinkedStoreServiceId = string.IsNullOrWhiteSpace(s.LinkedStoreServiceId)
                ? null
                : s.LinkedStoreServiceId.Trim(),
            Configured = s.Configured,
            TipoServicio = s.TipoServicio ?? "",
            TiempoStartDate = s.Tiempo?.StartDate ?? "",
            TiempoEndDate = s.Tiempo?.EndDate ?? "",
            Descripcion = s.Descripcion ?? "",
            Incluye = s.Incluye ?? "",
            NoIncluye = s.NoIncluye ?? "",
            Entregables = s.Entregables ?? "",
            MetodoPago = s.MetodoPago ?? "",
            Moneda = s.Moneda ?? "",
            MedicionCumplimiento = s.MedicionCumplimiento ?? "",
            PenalIncumplimiento = s.PenalIncumplimiento ?? "",
            NivelResponsabilidad = s.NivelResponsabilidad ?? "",
            PropIntelectual = s.PropIntelectual ?? "",
            ScheduleCalendarYear = s.Horarios?.CalendarYear ?? DateTime.UtcNow.Year,
            ScheduleDefaultWindowStart = s.Horarios?.DefaultWindow?.Start ?? "09:00",
            ScheduleDefaultWindowEnd = s.Horarios?.DefaultWindow?.End ?? "17:00",
            RiesgosEnabled = s.Riesgos?.Enabled ?? false,
            DependenciasEnabled = s.Dependencias?.Enabled ?? false,
            GarantiasEnabled = s.Garantias?.Enabled ?? false,
            GarantiasTexto = s.Garantias?.Texto ?? "",
            PenalAtrasoEnabled = s.PenalAtraso?.Enabled ?? false,
            PenalAtrasoTexto = s.PenalAtraso?.Texto ?? "",
            TerminacionEnabled = s.Terminacion?.Enabled ?? false,
            TerminacionAvisoDias = s.Terminacion?.AvisoDias ?? "",
        };
    }

    private static void AppendScheduleCollections(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        if (s.Horarios?.Months is { Count: > 0 } months)
        {
            foreach (var m in months.Distinct().Order())
            {
                if (m is >= 1 and <= 12)
                    row.ScheduleMonths.Add(new TradeAgreementServiceScheduleMonthRow
                        { ServiceItemId = serviceItemId, Month = m });
            }
        }

        if (s.Horarios?.DaysByMonth is { Count: > 0 } daysByMonth)
        {
            foreach (var (key, days) in daysByMonth)
            {
                if (!int.TryParse(key, out var month) || month is < 1 or > 12)
                    continue;
                foreach (var d in days.Distinct().Order())
                {
                    if (d is >= 1 and <= 31)
                        row.ScheduleDays.Add(new TradeAgreementServiceScheduleDayRow
                        {
                            ServiceItemId = serviceItemId,
                            Month = month,
                            CalendarDay = d,
                        });
                }
            }
        }

        if (s.Horarios?.DayHourOverrides is { Count: > 0 } overrides)
        {
            foreach (var (key, w) in overrides)
            {
                var parts = key.Split('-', 2);
                if (parts.Length != 2
                    || !int.TryParse(parts[0], out var om)
                    || !int.TryParse(parts[1], out var od))
                    continue;
                if (om is < 1 or > 12 || od is < 1 or > 31)
                    continue;
                row.ScheduleOverrides.Add(new TradeAgreementServiceScheduleOverrideRow
                {
                    ServiceItemId = serviceItemId,
                    Month = om,
                    CalendarDay = od,
                    WindowStart = w?.Start ?? "",
                    WindowEnd = w?.End ?? "",
                });
            }
        }
    }

    private static void AppendPaymentCollections(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        if (s.RecurrenciaPagos?.Months is { Count: > 0 } paymentMonths)
        {
            foreach (var m in paymentMonths.Distinct().Order())
            {
                if (m is >= 1 and <= 12)
                    row.PaymentMonths.Add(new TradeAgreementServicePaymentMonthRow
                        { ServiceItemId = serviceItemId, Month = m });
            }
        }

        var entryOrder = 0;
        if (s.RecurrenciaPagos?.Entries is { Count: > 0 } entries)
        {
            foreach (var e in entries)
            {
                row.PaymentEntries.Add(new TradeAgreementServicePaymentEntryRow
                {
                    Id = TradeAgreementEntityIdFactory.NewId("pay"),
                    ServiceItemId = serviceItemId,
                    SortOrder = entryOrder++,
                    Month = e.Month,
                    Day = e.Day,
                    Amount = e.Amount ?? "",
                });
            }
        }
    }

    private static void AppendRiesgos(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        var i = 0;
        if (s.Riesgos?.Items is not { Count: > 0 } items)
            return;
        foreach (var t in items.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            row.RiesgoItems.Add(new TradeAgreementServiceRiesgoRow
            {
                Id = TradeAgreementEntityIdFactory.NewId("rie"),
                ServiceItemId = serviceItemId,
                SortOrder = i++,
                Text = t.Trim(),
            });
        }
    }

    private static void AppendDependencias(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        var i = 0;
        if (s.Dependencias?.Items is not { Count: > 0 } items)
            return;
        foreach (var t in items.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            row.DependenciaItems.Add(new TradeAgreementServiceDependenciaRow
            {
                Id = TradeAgreementEntityIdFactory.NewId("dep"),
                ServiceItemId = serviceItemId,
                SortOrder = i++,
                Text = t.Trim(),
            });
        }
    }

    private static void AppendTerminacionCausas(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        var i = 0;
        if (s.Terminacion?.Causas is not { Count: > 0 } causas)
            return;
        foreach (var t in causas.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            row.TerminacionCausas.Add(new TradeAgreementServiceTerminacionCausaRow
            {
                Id = TradeAgreementEntityIdFactory.NewId("tc"),
                ServiceItemId = serviceItemId,
                SortOrder = i++,
                Text = t.Trim(),
            });
        }
    }

    private static void AppendMonedasAceptadas(
        TradeAgreementServiceItemRow row,
        string serviceItemId,
        ServiceItemRequest s)
    {
        var i = 0;
        if (s.MonedasAceptadas is not { Count: > 0 } mons)
            return;
        foreach (var c in mons.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            row.MonedasAceptadas.Add(new TradeAgreementServiceMonedaRow
            {
                Id = TradeAgreementEntityIdFactory.NewId("mon"),
                ServiceItemId = serviceItemId,
                SortOrder = i++,
                Code = c.Trim(),
            });
        }
    }
}
