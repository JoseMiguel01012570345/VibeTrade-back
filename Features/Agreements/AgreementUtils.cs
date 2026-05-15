using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stripe;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements.Dtos;
using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Agreements;

public static class AgreementUtils
{
    private static readonly JsonSerializerOptions CondicionesExtrasReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions CondicionesExtrasJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string NewEntityId(string prefix)
    {
        var s = $"{prefix}_{Guid.NewGuid():N}";
        return s.Length <= 64 ? s : s[..64];
    }

    public static string TrimEntityId(string id, int max) =>
        id.Length <= max ? id : id[..max];

    public static string NewAgreementRowId() => "agr_" + Guid.NewGuid().ToString("N")[..16];

    public static string NormalizeAgreementTitle(string title) => (title ?? "").Trim().ToLowerInvariant();

    public static string NormalizeExtraValueKind(string? raw)
    {
        var k = (raw ?? "").Trim().ToLowerInvariant();
        return k is "image" or "document" ? k : "text";
    }

    public static bool IsSkippableEmptyExtraDraftRow(TradeAgreementExtraFieldRequest x)
    {
        var title = (x.Title ?? "").Trim();
        if (title.Length > 0)
            return false;

        var kind = NormalizeExtraValueKind(x.ValueKind);
        if (kind is "image" or "document")
            return string.IsNullOrWhiteSpace(x.MediaUrl);

        return string.IsNullOrWhiteSpace(x.TextValue);
    }

    public static bool ValidateDraft(TradeAgreementDraftRequest d)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length > 512)
            return false;
        if (d.IncludeMerchandise == d.IncludeService)
            return false;
        return ValidateExtraFields(d);
    }

    public static bool ValidateExtraFields(TradeAgreementDraftRequest d)
    {
        var list = d.ExtraFields;
        if (list is null || list.Count == 0)
            return true;
        if (list.Count > 48)
            return false;

        foreach (var x in list)
        {
            if (IsSkippableEmptyExtraDraftRow(x))
                continue;

            var title = (x.Title ?? "").Trim();
            if (title.Length < 1 || title.Length > 256)
                return false;

            var kind = NormalizeExtraValueKind(x.ValueKind);

            if (kind == "text")
            {
                var txt = (x.TextValue ?? "").Trim();
                if (txt.Length < 1 || txt.Length > 8000)
                    return false;
            }
            else
            {
                var url = (x.MediaUrl ?? "").Trim();
                if (url.Length < 24 || url.Length > 2048)
                    return false;
                if (!url.StartsWith("/api/v1/media/", StringComparison.Ordinal))
                    return false;
            }

            var fn = (x.FileName ?? "").Trim();
            if (fn.Length > 512)
                return false;
        }

        return true;
    }

    public static bool AgreementHasMerchandiseForRouteLink(TradeAgreementRow ag)
    {
        if (!ag.IncludeMerchandise)
            return false;
        foreach (var m in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            if (!TryParsePositiveDecimal(m.Cantidad, out _))
                continue;
            if (!TryParsePositiveDecimal(m.ValorUnitario, out _))
                continue;
            var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(m.Moneda ?? ag.MerchandiseMeta?.Moneda);
            if (string.IsNullOrEmpty(mon))
                continue;
            return true;
        }

        return false;
    }

    public static bool TryParsePositiveDecimal(string? raw, out decimal value)
    {
        value = 0;
        var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal)
            .Replace('\u00a0', ' ');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return false;
        value = d;
        return d > 0;
    }

    public static string StripeErrorUserMessage(StripeException sx) =>
        string.IsNullOrWhiteSpace(sx.StripeError?.Message) ? sx.Message : sx.StripeError!.Message;

    public static List<TradeAgreementExtraFieldApi> DeserializeCondicionesExtrasApi(string? raw)
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

    public static string? SerializeCondicionesExtrasJson(List<TradeAgreementExtraFieldRequest>? list)
    {
        if (list is null || list.Count == 0)
            return null;
        return JsonSerializer.Serialize(list, CondicionesExtrasJsonOpts);
    }

    public sealed record AgreementEvidenceSnapshot(string Text, List<ServiceEvidenceAttachmentBody> Atts);

    public static AgreementEvidenceSnapshot NormalizeEvidence(
        string? text,
        IReadOnlyList<ServiceEvidenceAttachmentBody>? atts)
    {
        var t = (text ?? "").Trim();
        var a = (atts ?? Array.Empty<ServiceEvidenceAttachmentBody>())
            .Select(x => new ServiceEvidenceAttachmentBody(
                (x.Id ?? "").Trim(),
                (x.Url ?? "").Trim(),
                (x.FileName ?? "").Trim(),
                (x.Kind ?? "").Trim()))
            .OrderBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AgreementEvidenceSnapshot(t, a);
    }

    public static bool EvidenceEquals(AgreementEvidenceSnapshot a, AgreementEvidenceSnapshot b)
    {
        if (!string.Equals(a.Text, b.Text, StringComparison.Ordinal))
            return false;
        if (a.Atts.Count != b.Atts.Count)
            return false;
        for (var i = 0; i < a.Atts.Count; i++)
        {
            var x = a.Atts[i];
            var y = b.Atts[i];
            if (!string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
