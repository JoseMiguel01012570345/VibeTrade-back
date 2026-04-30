using System.Globalization;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Replica la lógica de <c>paymentCheckoutBreakdown.ts</c> para validar montos en servidor.</summary>
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
        string? RouteStopId);

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

    /// <exception cref="ArgumentException"></exception>
    private static decimal ParseDecimal(string? raw)
    {
        var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal).Replace('\u00a0', ' ');
        return decimal.TryParse(t, CultureInfo.InvariantCulture, out var d) ? d : throw new ArgumentException("invalid_decimal");
    }

    public static BreakdownDto ComputeForAgreement(
        TradeAgreementRow ag,
        RouteSheetPayload? routeSheetPayload)
    {
        var errs = new List<string>();
        if (!string.Equals(ag.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            errs.Add("El acuerdo debe estar aceptado.");

        if (ag.DeletedAtUtc is not null)
            errs.Add("El acuerdo fue eliminado.");

        var buckets = new Dictionary<string, (List<BasisLineDto> Lines, long Sub)>(
            StringComparer.OrdinalIgnoreCase);

        void Push(string cat, string label, string cur3, decimal amountMajor, string? rsId = null, string? stopId = null)
        {
            if (amountMajor <= 0) return;
            var curLower = NormalizeCurrency(cur3);
            if (string.IsNullOrEmpty(curLower)) return;
            var minor = MajorToMinor(amountMajor, curLower);
            if (minor <= 0) return;
            if (!buckets.TryGetValue(curLower, out var b))
            {
                b = (new List<BasisLineDto>(), 0);
                buckets[curLower] = b;
            }
            b.Lines.Add(new BasisLineDto(cat, label, curLower, minor, rsId, stopId));
            b.Sub += minor;
            buckets[curLower] = (b.Lines, b.Sub);
        }

        if (ag.IncludeMerchandise)
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

                Push("merchandise", $"{m.Tipo} (× {m.Cantidad})", mon, q * vu);
            }
        }

        if (ag.IncludeService)
        {
            foreach (var svc in ag.ServiceItems.OrderBy(x => x.SortOrder))
            {
                if (!svc.Configured) continue;
                var entries = svc.PaymentEntries.OrderBy(e => e.SortOrder).ToList();
                if (entries.Count == 0) continue;
                TradeAgreementServicePaymentEntryRow? picked;
                try
                {
                    picked = PickFirstInsideVigencia(
                        svc.TiempoStartDate,
                        svc.TiempoEndDate,
                        entries);
                }
                catch
                {
                    continue;
                }

                if (picked is null) continue;
                decimal amt;
                try
                {
                    amt = ParseDecimal(picked.Amount);
                }
                catch
                {
                    continue;
                }

                var mon = NormalizeCurrencyFirst(picked.Moneda);
                if (string.IsNullOrEmpty(mon))
                    continue;

                Push(
                    "service_installment",
                    $"Primera cuota — {svc.TipoServicio}",
                    mon,
                    amt);
            }
        }

        string? rsIdLink = string.IsNullOrWhiteSpace(ag.RouteSheetId) ? null : ag.RouteSheetId!.Trim();

        if (!string.IsNullOrEmpty(rsIdLink) && routeSheetPayload?.Paradas is { Count: > 0 } stops)
        {
            foreach (var p in stops.OrderBy(x => x.Orden))
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                decimal amt;
                try
                {
                    amt = ParseDecimal(p.PrecioTransportista ?? "");
                }
                catch { continue; }

                var mono = NormalizeCurrencyFirst(p.MonedaPago) ?? NormalizeCurrencyFirst(routeSheetPayload.MonedaPago ?? "");
                if (string.IsNullOrEmpty(mono))
                    continue;

                var legDesc = $"{p.Origen} → {p.Destino}";
                Push("route_leg", $"Transporte — {legDesc}".Trim(),
                    mono, amt,
                    rsIdLink, p.Id?.Trim());
            }
        }
        // Si hay RouteSheetId pero no payload / sin paradas: omitimos tramos (igual que sin vínculo).
        // El cobro sigue por mercancía/servicio; solo fallamos si no queda ningún importe.

        var byCurrency = new List<CurrencyTotalsDto>();

        foreach (var kv in buckets.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var curLower = kv.Key;
            var (lines, sub) = kv.Value;
            if (sub <= 0) continue;
            var climate = ClimateMinorFromSubtotal(sub);
            var stripeFee = StripeFeeEstimate(sub + climate, curLower);
            var total = sub + climate + stripeFee;
            byCurrency.Add(new CurrencyTotalsDto(
                curLower,
                sub,
                climate,
                stripeFee,
                total,
                lines));
        }

        if (byCurrency.Count == 0 && errs.Count == 0)
            errs.Add("No hay importes para cobrar en este acuerdo.");

        return new BreakdownDto(errs.Count == 0, errs, byCurrency);
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
        if (orderedEntries.Count == 0)
            return null;
        if (!DateOnly.TryParse((startDate ?? "").Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dStart) ||
            !DateOnly.TryParse((endDate ?? "").Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dEnd))
        {
            return orderedEntries.OrderBy(e => e.Month).ThenBy(e => e.Day).First();
        }

        if (dStart > dEnd)
            return orderedEntries.OrderBy(e => e.Month).ThenBy(e => e.Day).First();

        DateOnly bestDate = DateOnly.MaxValue;
        TradeAgreementServicePaymentEntryRow? best = null;

        foreach (var e in orderedEntries)
        {
            for (var y = dStart.Year; y <= dEnd.Year; y++)
            {
                DateOnly cand;
                try
                {
                    cand = new DateOnly(y, e.Month, e.Day);
                }
                catch
                {
                    continue;
                }

                if (cand < dStart || cand > dEnd) continue;

                if (best is null || cand < bestDate)
                {
                    bestDate = cand;
                    best = e;
                }

                // Una fecha por año suficiente (entradas válidas año a año).
                break;
            }
        }

        if (best != null)
            return best;

        return orderedEntries.OrderBy(ev => ev.Month).ThenBy(ev => ev.Day).First();
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

    // Stripe: 2.9 % + fijo opcional sobre (subtotal+climate).
    public static long StripeFeeEstimate(long subPlusClimateMinor, string currencyLower)
    {
        if (subPlusClimateMinor <= 0) return 0;
        var pctPart = (long)Math.Ceiling(subPlusClimateMinor * 0.029m - 0.000001m);
        var cf = currencyLower.ToLowerInvariant() switch { "usd" or "eur" or "ars" => 30L, _ => 0L };
        return pctPart + cf;
    }
}
