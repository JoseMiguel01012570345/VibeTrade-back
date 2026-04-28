namespace VibeTrade.Backend.Infrastructure.DemoData;

/// <summary>Root document for <c>demo-seed.json</c>.</summary>
public sealed class DemoSeedDocument
{
    /// <summary>Version marker for logs; seed is idempotent by user ids.</summary>
    public string IdempotencyKey { get; set; } = "";

    public List<DemoUserSeed> Users { get; set; } = new();
}

public sealed class DemoUserSeed
{
    public string Id { get; set; } = "";

    public string PhoneDigits { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? PhoneDisplay { get; set; }

    public string? AvatarUrl { get; set; }

    public int TrustScore { get; set; } = 50;

    public List<DemoStoreSeed> Stores { get; set; } = new();
}

public sealed class DemoStoreSeed
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Verified { get; set; }

    public bool TransportIncluded { get; set; }

    public int TrustScore { get; set; } = 80;

    public List<string> Categories { get; set; } = new();

    public string Pitch { get; set; } = "";

    public long JoinedAtMs { get; set; }

    public DemoLocationSeed? Location { get; set; }

    public string? WebsiteUrl { get; set; }

    public List<DemoProductSeed> Products { get; set; } = new();

    public List<DemoServiceSeed> Services { get; set; } = new();
}

public sealed class DemoLocationSeed
{
    public double Lat { get; set; }

    public double Lng { get; set; }
}

public sealed class DemoProductSeed
{
    public string Id { get; set; } = "";

    public string Category { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Model { get; set; }

    public string ShortDescription { get; set; } = "";

    public string MainBenefit { get; set; } = "";

    public string TechnicalSpecs { get; set; } = "";

    public string Condition { get; set; } = "Nuevo";

    public string Price { get; set; } = "";

    public string? MonedaPrecio { get; set; }

    public string MonedasJson { get; set; } = """["USD"]""";

    public string? TaxesShippingInstall { get; set; }

    public string Availability { get; set; } = "";

    public string WarrantyReturn { get; set; } = "";

    public string ContentIncluded { get; set; } = "";

    public string UsageConditions { get; set; } = "";

    public bool Published { get; set; } = true;

    public string PhotoUrlsJson { get; set; } = "[]";

    public string CustomFieldsJson { get; set; } = "[]";
}

public sealed class DemoServiceSeed
{
    public string Id { get; set; } = "";

    public string Category { get; set; } = "";

    public string TipoServicio { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public string Incluye { get; set; } = "";

    public string NoIncluye { get; set; } = "";

    public string Entregables { get; set; } = "";

    public string PropIntelectual { get; set; } = "";

    public string MonedasJson { get; set; } = """["USD"]""";

    public bool? Published { get; set; }

    public string CustomFieldsJson { get; set; } = "[]";

    public string PhotoUrlsJson { get; set; } = "[]";
}
