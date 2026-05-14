using System.Collections.Generic;
using System.Globalization;
using VibeTrade.Backend.Data.Entities;
namespace VibeTrade.Backend.Features.Payments;

/// <summary>
/// Replica la lógica de <c>paymentCheckoutBreakdown.ts</c> para validar montos en servidor.
/// El importe cobrado (<see cref="CurrencyTotalsDto.TotalMinor"/>) es solo el subtotal de ítems;
/// Climate y Stripe en el DTO son referencias informativas (no se suman al PaymentIntent).
/// </summary>
public static class PaymentCheckoutComputation
{
    private static readonly HashSet<string> ZeroDecimalStripe = new[]
    {
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf",
    }.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public sealed record BasisLineDto(
        string Category,
        string Label,
        string CurrencyLower,
        long AmountMinor,
        string? RouteSheetId,
        string? RouteStopId,
        string? MerchandiseLineId = null);

    public sealed record CurrencyTotalsDto(
        string CurrencyLower,
        long SubtotalMinor,
        long ClimateMinor,
        long StripeFeeMinor,
        long TotalMinor,
        IReadOnlyList<BasisLineDto> Lines);

    public sealed record BreakdownDto(
        bool Ok,
        IReadOnlyList<string> Errors,
        IReadOnlyList<CurrencyTotalsDto> ByCurrency);

    public sealed record ServicePaymentPickDto(
        string ServiceItemId,
        int EntryMonth,
        int EntryDay);

    /// <exception cref="ArgumentException"></exception>
    private static decimal ParseDecimal(string? raw)
    {
        var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal).Replace('\u00a0', ' ');
        return decimal.TryParse(t, CultureInfo.InvariantCulture, out var d) ? d : throw new ArgumentException("invalid_decimal");
    }

    public static BreakdownDto ComputeForAgreement(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments = null,
        IReadOnlyList<string>? selectedRouteStopIds = null,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        IReadOnlyDictionary<string, HashSet<string>>? paidMerchandiseLineIdsByCurrencyLower = null)
    {
        var errs = ValidateAgreementForCheckout(ag);
        var buckets = new Dictionary<string, CurrencyBucket>(StringComparer.OrdinalIgnoreCase);
        var hasServiceSelection = selectedServicePayments is { Count: > 0 };
        var routesSpecified = selectedRouteStopIds is not null;
        var routesFiltered = routesSpecified && selectedRouteStopIds!.Count > 0;

        var merchSpecified = selectedMerchandiseLineIds is not null;
        var merchFiltered = merchSpecified && selectedMerchandiseLineIds!.Count > 0;

        // Mercadería: selección explícita (POST) o comportamiento legacy (GET sin picks / sin tramos filtrados).
        if (ag.IncludeMerchandise && !hasServiceSelection)
        {
            if (merchSpecified)
            {
                if (merchFiltered)
                    AccumulateMerchandiseFiltered(
                        ag, selectedMerchandiseLineIds!, errs, buckets, paidMerchandiseLineIdsByCurrencyLower);
                // explícito vacío → no suma líneas de mercadería
            }
            else if (!routesFiltered)
            {
                AccumulateMerchandise(ag, errs, buckets, paidMerchandiseLineIdsByCurrencyLower);
            }
        }

        if (ag.IncludeService)
        {
            // Acuerdo mixto: si el cliente envía selección explícita de mercadería o tramos y no eligió cuotas,
            // no aplicar la "primera cuota" automática de servicios (evita cobrar servicio al pagar solo mercadería/tramos).
            var partialMerchOrRouteContext = merchSpecified || routesSpecified;
            if (partialMerchOrRouteContext && !hasServiceSelection)
            {
                /* omitir servicios en este desglose */
            }
            else
                AccumulateServices(ag, selectedServicePayments, errs, buckets);
        }

        if (!hasServiceSelection)
            AccumulateRouteLegsIfAny(ag, routeSheetPayload, selectedRouteStopIds, buckets);

        var byCurrency = BuildTotalsByCurrency(buckets);
        if (byCurrency.Count == 0 && errs.Count == 0)
            errs.Add("No hay importes para cobrar en este acuerdo.");

        return new BreakdownDto(errs.Count == 0, errs, byCurrency);
    }

    private sealed record CurrencyBucket(List<BasisLineDto> Lines, long SubtotalMinor);

    private static List<string> ValidateAgreementForCheckout(TradeAgreementRow ag)
    {
        var errs = new List<string>();
        if (!string.Equals(ag.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            errs.Add("El acuerdo debe estar aceptado.");
        if (ag.DeletedAtUtc is not null)
            errs.Add("El acuerdo fue eliminado.");
        return errs;
    }

    private static void PushLine(
        IDictionary<string, CurrencyBucket> buckets,
        string cat,
        string label,
        string currencyIso3,
        decimal amountMajor,
        string? routeSheetId = null,
        string? routeStopId = null,
        string? merchandiseLineId = null)
    {
        if (amountMajor <= 0) return;
        var curLower = NormalizeCurrency(currencyIso3);
        if (string.IsNullOrEmpty(curLower)) return;
        var minor = MajorToMinor(amountMajor, curLower);
        if (minor <= 0) return;

        if (!buckets.TryGetValue(curLower, out var b))
            b = new CurrencyBucket(new List<BasisLineDto>(), 0);

        b.Lines.Add(new BasisLineDto(cat, label, curLower, minor, routeSheetId, routeStopId, merchandiseLineId));
        buckets[curLower] = b with { SubtotalMinor = b.SubtotalMinor + minor };
    }

    private static bool MerchandiseLineAlreadyPaidInCurrency(
        IReadOnlyDictionary<string, HashSet<string>>? paidMerchandiseLineIdsByCurrencyLower,
        string currencyIso3,
        string merchandiseLineId)
    {
        if (paidMerchandiseLineIdsByCurrencyLower is null || paidMerchandiseLineIdsByCurrencyLower.Count == 0)
            return false;
        var cur = NormalizeCurrency(currencyIso3);
        if (string.IsNullOrEmpty(cur)) return false;
        var mid = merchandiseLineId.Trim();
        if (mid.Length == 0) return false;
        return paidMerchandiseLineIdsByCurrencyLower.TryGetValue(cur, out var set) && set.Contains(mid);
    }

    private static void AccumulateMerchandise(
        TradeAgreementRow ag,
        ICollection<string> errs,
        IDictionary<string, CurrencyBucket> buckets,
        IReadOnlyDictionary<string, HashSet<string>>? paidMerchandiseLineIdsByCurrencyLower)
    {
        foreach (var m in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            decimal q;
            decimal vu;
            try
            {
                q = ParseDecimal(m.Cantidad);
                vu = ParseDecimal(m.ValorUnitario);
            }
            catch
            {
                continue;
            }

            var mon = NormalizeCurrencyFirst(m.Moneda ?? ag.MerchandiseMeta?.Moneda);
            if (string.IsNullOrEmpty(mon))
            {
                errs.Add("Mercancía: falta moneda.");
                continue;
            }

            if (q <= 0 || vu <= 0) continue;
            var mid = (m.Id ?? "").Trim();
            if (MerchandiseLineAlreadyPaidInCurrency(paidMerchandiseLineIdsByCurrencyLower, mon, mid))
                continue;
            PushLine(
                buckets,
                "merchandise",
                $"{m.Tipo} (× {m.Cantidad})",
                mon,
                q * vu,
                null,
                null,
                mid.Length > 0 ? mid : null);
        }
    }

    private static void AccumulateMerchandiseFiltered(
        TradeAgreementRow ag,
        IReadOnlyList<string> selectedIds,
        ICollection<string> errs,
        IDictionary<string, CurrencyBucket> buckets,
        IReadOnlyDictionary<string, HashSet<string>>? paidMerchandiseLineIdsByCurrencyLower)
    {
        var pick = new HashSet<string>(
            selectedIds
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0),
            StringComparer.Ordinal);
        foreach (var m in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            var mid = (m.Id ?? "").Trim();
            if (mid.Length == 0 || !pick.Contains(mid))
                continue;

            decimal q;
            decimal vu;
            try
            {
                q = ParseDecimal(m.Cantidad);
                vu = ParseDecimal(m.ValorUnitario);
            }
            catch
            {
                continue;
            }

            var mon = NormalizeCurrencyFirst(m.Moneda ?? ag.MerchandiseMeta?.Moneda);
            if (string.IsNullOrEmpty(mon))
            {
                errs.Add("Mercancía: falta moneda.");
                continue;
            }

            if (q <= 0 || vu <= 0) continue;
            if (MerchandiseLineAlreadyPaidInCurrency(paidMerchandiseLineIdsByCurrencyLower, mon, mid))
                continue;
            PushLine(buckets, "merchandise", $"{m.Tipo} (× {m.Cantidad})", mon, q * vu, null, null, mid);
        }
    }

    private static void AccumulateServices(
        TradeAgreementRow ag,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        ICollection<string> errs,
        IDictionary<string, CurrencyBucket> buckets)
    {
        var picks = BuildServicePickIndex(selectedServicePayments);
        var anyPicked = false;

        foreach (var svc in ag.ServiceItems.OrderBy(x => x.SortOrder))
        {
            if (!svc.Configured) continue;
            var entries = svc.PaymentEntries.OrderBy(e => e.SortOrder).ToList();
            if (entries.Count == 0) continue;

            if (picks is not null)
            {
                anyPicked |= PushPickedEntriesForService(svc, entries, picks, buckets);
                continue;
            }

            PushFirstServiceInstallmentIfAny(svc, entries, buckets);
        }

        if (picks is not null && !anyPicked)
            errs.Add("No se pudo determinar la cuota seleccionada para los servicios.");
    }

    private static Dictionary<string, List<(int Month, int Day)>>? BuildServicePickIndex(
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments)
    {
        if (selectedServicePayments is not { Count: > 0 }) return null;
        return selectedServicePayments
            .Where(p => !string.IsNullOrWhiteSpace(p.ServiceItemId))
            .GroupBy(p => p.ServiceItemId.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(x => (Month: x.EntryMonth, Day: x.EntryDay))
                    .Where(x => x.Month > 0 && x.Day > 0)
                    .Distinct()
                    .ToList(),
                StringComparer.Ordinal);
    }

    private static bool PushPickedEntriesForService(
        TradeAgreementServiceItemRow svc,
        IReadOnlyList<TradeAgreementServicePaymentEntryRow> entries,
        IReadOnlyDictionary<string, List<(int Month, int Day)>> picks,
        IDictionary<string, CurrencyBucket> buckets)
    {
        if (!picks.TryGetValue(svc.Id.Trim(), out var keys) || keys.Count == 0)
            return false;

        var pushedAny = false;
        foreach (var key in keys)
        {
            var pickedEntry = entries.FirstOrDefault(e => e.Month == key.Month && e.Day == key.Day);
            if (pickedEntry is null) continue;
            if (!TryParseServiceEntry(pickedEntry, out var pickedAmt, out var pickedMon))
                continue;

            PushLine(
                buckets,
                "service_installment",
                $"Cuota — {svc.TipoServicio} (mes {pickedEntry.Month} día {pickedEntry.Day})",
                pickedMon,
                pickedAmt);
            pushedAny = true;
        }

        return pushedAny;
    }

    private static void PushFirstServiceInstallmentIfAny(
        TradeAgreementServiceItemRow svc,
        IReadOnlyList<TradeAgreementServicePaymentEntryRow> entries,
        IDictionary<string, CurrencyBucket> buckets)
    {
        TradeAgreementServicePaymentEntryRow? picked;
        try
        {
            picked = PickFirstInsideVigencia(svc.TiempoStartDate, svc.TiempoEndDate, entries);
        }
        catch
        {
            return;
        }

        if (picked is null) return;
        if (!TryParseServiceEntry(picked, out var firstAmt, out var firstMon))
            return;

        PushLine(
            buckets,
            "service_installment",
            $"Primera cuota — {svc.TipoServicio}",
            firstMon,
            firstAmt);
    }

    private static bool TryParseServiceEntry(
        TradeAgreementServicePaymentEntryRow entry,
        out decimal amountMajor,
        out string currencyIso3)
    {
        amountMajor = 0;
        currencyIso3 = "";
        try
        {
            amountMajor = ParseDecimal(entry.Amount);
        }
        catch
        {
            return false;
        }

        var mon = NormalizeCurrencyFirst(entry.Moneda);
        if (string.IsNullOrEmpty(mon)) return false;
        currencyIso3 = mon;
        return amountMajor > 0;
    }

    private static void AccumulateRouteLegsIfAny(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload,
        IReadOnlyList<string>? selectedRouteStopIds,
        IDictionary<string, CurrencyBucket> buckets)
    {
        var rsIdLink = string.IsNullOrWhiteSpace(ag.RouteSheetId) ? null : ag.RouteSheetId!.Trim();
        if (string.IsNullOrEmpty(rsIdLink)) return;
        if (routeSheetPayload?.Paradas is not { Count: > 0 } stops) return;

        // null = sin filtro (todos los tramos con importe); lista vacía = ningún tramo; lista con ids = filtro.
        HashSet<string>? stopPick = null;
        if (selectedRouteStopIds is not null)
        {
            stopPick = new HashSet<string>(
                selectedRouteStopIds
                    .Select(x => (x ?? "").Trim())
                    .Where(x => x.Length > 0),
                StringComparer.Ordinal);
        }

        foreach (var p in stops.OrderBy(x => x.Orden))
        {
            if (string.IsNullOrWhiteSpace(p.Id)) continue;
            var pid = p.Id.Trim();
            if (stopPick is not null && !stopPick.Contains(pid))
                continue;
            if (!TryParseRouteStopAmount(p, routeSheetPayload, out var amt, out var mon))
                continue;

            var legDesc = $"{p.Origen} → {p.Destino}";
            PushLine(
                buckets,
                "route_leg",
                $"Transporte — {legDesc}".Trim(),
                mon,
                amt,
                rsIdLink,
                p.Id?.Trim());
        }
    }

    private static bool TryParseRouteStopAmount(
        RouteStopPayload p,
        RouteSheetPayload routeSheetPayload,
        out decimal amountMajor,
        out string currencyIso3)
    {
        amountMajor = 0;
        currencyIso3 = "";
        try
        {
            amountMajor = ParseDecimal(p.PrecioTransportista ?? "");
        }
        catch
        {
            return false;
        }

        var mon = NormalizeCurrencyFirst(p.MonedaPago) ??
                  NormalizeCurrencyFirst(routeSheetPayload.MonedaPago ?? "");
        if (string.IsNullOrEmpty(mon)) return false;
        currencyIso3 = mon;
        return amountMajor > 0;
    }

    private static List<CurrencyTotalsDto> BuildTotalsByCurrency(
        IDictionary<string, CurrencyBucket> buckets)
    {
        var byCurrency = new List<CurrencyTotalsDto>();
        foreach (var kv in buckets.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var curLower = kv.Key;
            var b = kv.Value;
            if (b.SubtotalMinor <= 0) continue;
            var climate = ClimateMinorFromSubtotal(b.SubtotalMinor);
            var stripeFee = StripeFeeEstimate(b.SubtotalMinor, curLower);
            var total = b.SubtotalMinor;
            byCurrency.Add(new CurrencyTotalsDto(
                curLower,
                b.SubtotalMinor,
                climate,
                stripeFee,
                total,
                b.Lines));
        }
        return byCurrency;
    }

    internal static CurrencyTotalsDto? GetCurrencyBucket(BreakdownDto breakdown, string currencyLower)
    {
        return breakdown.ByCurrency.FirstOrDefault(c =>
            string.Equals(c.CurrencyLower, currencyLower.Trim().ToLowerInvariant(), StringComparison.Ordinal));
    }

    internal static TradeAgreementServicePaymentEntryRow? PickFirstInsideVigencia(
        string startDate,
        string endDate,
        IReadOnlyList<TradeAgreementServicePaymentEntryRow> orderedEntries)
    {
        if (orderedEntries.Count == 0) return null;
        var fallback = orderedEntries.OrderBy(e => e.Month).ThenBy(e => e.Day).First();
        if (!TryParseVigencia(startDate, endDate, out var dStart, out var dEnd))
            return fallback;
        if (dStart > dEnd) return fallback;

        var best = PickFirstEntryDateWithinRange(dStart, dEnd, orderedEntries);
        return best ?? fallback;
    }

    private static bool TryParseVigencia(
        string startDate,
        string endDate,
        out DateOnly dStart,
        out DateOnly dEnd)
    {
        dStart = default;
        dEnd = default;
        return DateOnly.TryParse((startDate ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dStart) &&
               DateOnly.TryParse((endDate ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dEnd);
    }

    private static TradeAgreementServicePaymentEntryRow? PickFirstEntryDateWithinRange(
        DateOnly dStart,
        DateOnly dEnd,
        IReadOnlyList<TradeAgreementServicePaymentEntryRow> orderedEntries)
    {
        DateOnly bestDate = DateOnly.MaxValue;
        TradeAgreementServicePaymentEntryRow? best = null;

        foreach (var e in orderedEntries)
        {
            var cand = FindFirstOccurrenceWithinRange(dStart, dEnd, e.Month, e.Day);
            if (cand is null) continue;
            if (best is null || cand.Value < bestDate)
            {
                bestDate = cand.Value;
                best = e;
            }
        }

        return best;
    }

    private static DateOnly? FindFirstOccurrenceWithinRange(
        DateOnly start,
        DateOnly end,
        int month,
        int day)
    {
        for (var y = start.Year; y <= end.Year; y++)
        {
            DateOnly cand;
            try
            {
                cand = new DateOnly(y, month, day);
            }
            catch
            {
                continue;
            }

            if (cand < start || cand > end) continue;
            return cand;
        }
        return null;
    }

    private static DateTime? ParseIsoUtc(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(t, "^([0-9]{4})-([0-9]{2})-([0-9]{2})$");
        if (!m.Success) return null;
        var y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
    }

    public static string? NormalizeCurrencyFirst(string? s)
    {
        var t = (s ?? "").Trim().ToUpperInvariant();
        return t.Length is >= 3 and <= 8 ? t[..Math.Min(3, t.Length)] : null;
    }

    public static string NormalizeCurrency(string iso3Upper)
        => NormalizeCurrencyFirst(iso3Upper)?.ToLowerInvariant() ?? "";

    public static int StripeMinorDecimals(string currencyLower)
        => ZeroDecimalStripe.Contains(currencyLower) ? 0 : 2;

    internal static long MajorToMinor(decimal maj, string currencyLower)
    {
        var pow = StripeMinorDecimals(currencyLower);
        if (pow == 0) return decimal.ToInt64(decimal.Round(maj, MidpointRounding.AwayFromZero));
        return decimal.ToInt64(decimal.Round(maj * Power10(pow), MidpointRounding.AwayFromZero));
    }

    private static decimal Power10(int p)
    {
        var x = 1m;
        for (var i = 0; i < p; i++) x *= 10;
        return x;
    }

    // Climate 0.05 % del subtotal (minor units), como en cliente.
    public static long ClimateMinorFromSubtotal(long subtotalMinor)
        => subtotalMinor <= 0
            ? 0
            : (long)Math.Ceiling(subtotalMinor * 0.0005m - 0.000001m);

    /// <summary>2.9 % + fijo opcional sobre el subtotal cobrado (referencia; no se añade al importe del PI).</summary>
    public static long StripeFeeEstimate(long subtotalMinor, string currencyLower)
    {
        if (subtotalMinor <= 0) return 0;
        var pctPart = (long)Math.Ceiling(subtotalMinor * 0.029m - 0.000001m);
        var cf = currencyLower.ToLowerInvariant() switch { "usd" or "eur" => 30L, _ => 0L };
        return pctPart + cf;
    }
}
