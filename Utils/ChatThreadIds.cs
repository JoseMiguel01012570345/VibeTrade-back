namespace VibeTrade.Backend.Utils;

/// <summary>Ids de hilo normalizados como en BDD: <c>cth_</c> + hex de Guid (minúsculas, formato <c>N</c> con prefijo fijo <c>cth_</c> en minúscula).</summary>
public static class ChatThreadIds
{
    /// <summary>Coincide con el id guardado aunque la URL o un proxy alteren el casing del sufijo.</summary>
    public static string NormalizePersistedId(string? threadId)
    {
        var s = (threadId ?? "").Trim();
        if (s.Length < 4)
            return s;
        if (!s.StartsWith("cth_", StringComparison.OrdinalIgnoreCase))
            return s;
        var rest = s.Length > 4 ? s[4..] : "";
        return "cth_" + rest.ToLowerInvariant();
    }
}
