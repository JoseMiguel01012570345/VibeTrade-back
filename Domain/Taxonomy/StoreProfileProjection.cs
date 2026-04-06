namespace VibeTrade.Backend.Domain.Taxonomy;

/// <summary>Proyección de dominio desde JSON de workspace; no es entidad EF.</summary>
public sealed class StoreProfileProjection : CommercialActorBase, IStoreProfile
{
    public required IReadOnlyList<string> Categories { get; init; }
    public bool Verified { get; init; }
    public bool TransportIncluded { get; init; }

    public static StoreProfileProjection? TryFromStoreJson(System.Text.Json.JsonElement el)
    {
        if (el.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        var id = el.TryGetProperty("id", out var idP) ? idP.GetString() : null;
        var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return null;
        var cats = new List<string>();
        if (el.TryGetProperty("categories", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var x in c.EnumerateArray())
            {
                var s = x.GetString();
                if (!string.IsNullOrEmpty(s)) cats.Add(s);
            }
        }
        var trust = el.TryGetProperty("trustScore", out var t) && t.TryGetInt32(out var ts) ? ts : 0;
        var ver = el.TryGetProperty("verified", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
        var tr = el.TryGetProperty("transportIncluded", out var trP) && trP.ValueKind == System.Text.Json.JsonValueKind.True;
        return new StoreProfileProjection
        {
            Id = id,
            DisplayName = name,
            TrustScore = trust,
            Categories = cats,
            Verified = ver,
            TransportIncluded = tr,
        };
    }
}
