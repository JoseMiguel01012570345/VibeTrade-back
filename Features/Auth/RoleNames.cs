namespace VibeTrade.Backend.Features.Auth;

/// <summary>
/// Modelo de roles ligero (sin ASP.NET Identity). El dueño de una tienda es <see cref="SuperAdmin"/>
/// y puede asignar <see cref="Admin"/>, <see cref="Almacen"/> y <see cref="Afiliado"/> a otros usuarios.
/// </summary>
public static class RoleNames
{
    public const string SuperAdmin = "superadmin";
    public const string Admin = "admin";
    public const string Almacen = "almacen";
    public const string Afiliado = "afiliado";

    /// <summary>Roles que un superadmin puede asignar/quitar desde el módulo de Usuarios.</summary>
    public static readonly IReadOnlyList<string> AssignableByOwner = new[] { Admin, Almacen, Afiliado };

    /// <summary>Roles con acceso a paneles administrativos (finanzas, estadísticas, usuarios).</summary>
    public static readonly IReadOnlyList<string> AdminRoles = new[] { SuperAdmin, Admin };

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["superadmin"] = SuperAdmin,
            ["super_admin"] = SuperAdmin,
            ["admin"] = Admin,
            ["administrador"] = Admin,
            ["administrator"] = Admin,
            ["almacen"] = Almacen,
            ["almacén"] = Almacen,
            ["warehouse"] = Almacen,
            ["afiliado"] = Afiliado,
            ["affiliate"] = Afiliado,
        };

    /// <summary>Normaliza una etiqueta de rol (p. ej. "Administrador") a su id canónico, o <c>null</c> si no es válida.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Aliases.TryGetValue(raw.Trim(), out var canonical) ? canonical : null;
    }

    /// <summary>Normaliza y filtra a los roles asignables por el dueño (elimina duplicados y desconocidos).</summary>
    public static List<string> SanitizeAssignable(IEnumerable<string>? roles)
    {
        var result = new List<string>();
        if (roles is null)
            return result;
        foreach (var r in roles)
        {
            var canonical = Normalize(r);
            if (canonical is null)
                continue;
            if (!AssignableByOwner.Contains(canonical))
                continue;
            if (!result.Contains(canonical))
                result.Add(canonical);
        }
        return result;
    }
}
