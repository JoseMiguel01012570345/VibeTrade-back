using System.Globalization;
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

public static class TradeAgreementDraftToEntityMapper
{
    public static void ReplaceContentFromDraft(TradeAgreementRow ag, TradeAgreementDraftRequest draft)
    {
        ag.MerchandiseLines.Clear();
        ag.MerchandiseMeta = null;
        ag.ServiceItems.Clear();
        ag.ExtraFields.Clear();

        ag.IncludeMerchandise = draft.IncludeMerchandise;
        ag.IncludeService = draft.IncludeService;

        if (draft.IncludeMerchandise)
            AddMerchandiseLines(ag, draft);

        if (draft.IncludeService)
            AddServiceItems(ag, draft);

        AppendExtraFieldsIfApplicable(ag, draft);
    }

    private static void AppendExtraFieldsIfApplicable(
        TradeAgreementRow ag,
        TradeAgreementDraftRequest draft)
    {
        if (draft.ExtraFields is null)
            return;

        var order = 0;
        foreach (var x in draft.ExtraFields)
        {
            if (AgreementUtils.IsSkippableEmptyExtraDraftRow(x))
                continue;

            var title = (x.Title ?? "").Trim();
            var kind = AgreementUtils.NormalizeExtraValueKind(x.ValueKind);
            ag.ExtraFields.Add(new TradeAgreementExtraFieldRow
            {
                Id = AgreementUtils.NewEntityId("xfe"),
                TradeAgreementId = ag.Id,
                SortOrder = order++,
                Title = title,
                ValueKind = kind,
                TextValue = kind == "text" ? (x.TextValue ?? "").Trim() : null,
                MediaUrl = kind is not "text"
                    ? (x.MediaUrl ?? "").Trim()
                    : null,
                FileName = string.IsNullOrWhiteSpace(x.FileName)
                    ? null
                    : x.FileName.Trim(),
            });
        }
    }

    private static void AddMerchandiseLines(TradeAgreementRow ag, TradeAgreementDraftRequest draft)
    {
        var order = 0;
        foreach (var line in draft.Merchandise)
        {
            ag.MerchandiseLines.Add(new TradeAgreementMerchandiseLineRow
            {
                Id = AgreementUtils.NewEntityId("aml"),
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
                ? AgreementUtils.NewEntityId("svi")
                : AgreementUtils.TrimEntityId(s.Id!, 80);

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
            CondicionesExtrasJson = AgreementUtils.SerializeCondicionesExtrasJson(s.CondicionesExtras),
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
                    Id = AgreementUtils.NewEntityId("pay"),
                    ServiceItemId = serviceItemId,
                    SortOrder = entryOrder++,
                    Month = e.Month,
                    Day = e.Day,
                    Amount = e.Amount ?? "",
                    Moneda = e.Moneda?.Trim() ?? "",
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
                Id = AgreementUtils.NewEntityId("rie"),
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
                Id = AgreementUtils.NewEntityId("dep"),
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
                Id = AgreementUtils.NewEntityId("tc"),
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
                Id = AgreementUtils.NewEntityId("mon"),
                ServiceItemId = serviceItemId,
                SortOrder = i++,
                Code = c.Trim(),
            });
        }
    }
}

public static class TradeAgreementEntityToApiMapper
{
    public static TradeAgreementApiResponse ToApiResponse(
        TradeAgreementRow ag,
        bool hasSucceededPayments = false,
        bool hasSucceededRoutePayments = false,
        bool hasAcceptedMerchandiseEvidence = false)
    {
        var resp = MapAgreementHeader(
            ag,
            hasSucceededPayments,
            hasSucceededRoutePayments,
            hasAcceptedMerchandiseEvidence);
        MapMerchandiseMeta(ag, resp);
        MapMerchandiseLines(ag, resp);
        MapServiceItems(ag, resp);
        MapExtraFields(ag, resp);
        return resp;
    }

    private static TradeAgreementApiResponse MapAgreementHeader(
        TradeAgreementRow ag,
        bool hasSucceededPayments,
        bool hasSucceededRoutePayments,
        bool hasAcceptedMerchandiseEvidence)
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
            HasSucceededRoutePayments = hasSucceededRoutePayments,
            HasAcceptedMerchandiseEvidence = hasAcceptedMerchandiseEvidence,
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
            CondicionesExtras = AgreementUtils.DeserializeCondicionesExtrasApi(s.CondicionesExtrasJson),
        };
    }

    private static HorariosApi MapHorarios(TradeAgreementServiceItemRow s)
    {
        var daysByMonth = new Dictionary<string, List<int>>();
        foreach (var g in s.ScheduleDays.GroupBy(x => x.Month).OrderBy(x => x.Key))
            daysByMonth[g.Key.ToString(CultureInfo.InvariantCulture)] = g.Select(x => x.CalendarDay).Order().ToList();

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
