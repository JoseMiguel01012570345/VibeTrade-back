using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Infrastructure.DemoData;

/// <summary>
/// Loads realistic demo accounts, stores and catalog from <c>demo-seed.json</c> (idempotent).
/// </summary>
public static class DemoDataSeed
{
    private const string DefaultRelativeDataFile = "Infrastructure/DemoData/demo-seed.json";

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task RunIfNeededAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        IHostEnvironment hostEnvironment,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("DemoSeed:Enabled", false))
            return;

        var relativePath = configuration["DemoSeed:DataFile"] ?? DefaultRelativeDataFile;
        var path = Path.Combine(hostEnvironment.ContentRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            logger.LogWarning("Demo seed: archivo no encontrado ({Path}).", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var doc = await JsonSerializer.DeserializeAsync<DemoSeedDocument>(stream, JsonReadOptions, cancellationToken);
        if (doc is null || doc.Users.Count == 0)
        {
            logger.LogWarning("Demo seed: JSON vacío o inválido ({Path}).", path);
            return;
        }

        if (configuration.GetValue("DemoSeed:RemoveLegacyDemoData", true))
            await LegacyDemoDataCleanup.RunAsync(db, cancellationToken);

        var expectedIds = doc.Users.Select(u => u.Id).ToList();
        var existingCount = await db.UserAccounts
            .CountAsync(u => expectedIds.Contains(u.Id), cancellationToken);
        if (existingCount >= doc.Users.Count)
        {
            var contactCount = await db.UserContacts.CountAsync(
                c => EF.Functions.Like(c.Id, "cuba_demo_uc_%"),
                cancellationToken);
            if (contactCount >= doc.Users.Count * 3)
            {
                logger.LogInformation(
                    "Demo seed: omitido (dataset {Key} ya cargado).",
                    doc.IdempotencyKey);
                return;
            }

            if (contactCount == 0)
            {
                AddCubaDemoContacts(db, doc.Users.Count, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Demo seed: contactos añadidos entre usuarios demo existentes.");
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var u in doc.Users)
        {
            if (string.IsNullOrWhiteSpace(u.Id) || string.IsNullOrWhiteSpace(u.PhoneDigits))
                continue;

            db.UserAccounts.Add(new UserAccount
            {
                Id = u.Id.Trim(),
                PhoneDigits = u.PhoneDigits.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Id : u.DisplayName.Trim(),
                PhoneDisplay = string.IsNullOrWhiteSpace(u.PhoneDisplay) ? null : u.PhoneDisplay.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(u.AvatarUrl) ? null : u.AvatarUrl.Trim(),
                SavedOfferIdsJson = "[]",
                TrustScore = u.TrustScore,
                CreatedAt = now,
                UpdatedAt = now,
            });

            foreach (var store in u.Stores)
            {
                if (string.IsNullOrWhiteSpace(store.Id) || string.IsNullOrWhiteSpace(store.Name))
                    continue;

                var norm = MarketStoreNameNormalizer.Normalize(store.Name);
                var categoriesJson = JsonSerializer.Serialize(store.Categories ?? new List<string>());
                db.Stores.Add(new StoreRow
                {
                    Id = store.Id.Trim(),
                    OwnerUserId = u.Id.Trim(),
                    Name = store.Name.Trim(),
                    NormalizedName = norm,
                    Verified = store.Verified,
                    TransportIncluded = store.TransportIncluded,
                    TrustScore = store.TrustScore,
                    CategoriesJson = categoriesJson,
                    Pitch = string.IsNullOrWhiteSpace(store.Pitch) ? "" : store.Pitch.Trim(),
                    JoinedAtMs = store.JoinedAtMs > 0 ? store.JoinedAtMs : now.ToUnixTimeMilliseconds(),
                    LocationLatitude = store.Location?.Lat,
                    LocationLongitude = store.Location?.Lng,
                    WebsiteUrl = string.IsNullOrWhiteSpace(store.WebsiteUrl) ? null : store.WebsiteUrl.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                var sid = store.Id.Trim();
                foreach (var p in store.Products)
                {
                    if (string.IsNullOrWhiteSpace(p.Id))
                        continue;
                    db.StoreProducts.Add(new StoreProductRow
                    {
                        Id = p.Id.Trim(),
                        StoreId = sid,
                        Category = p.Category,
                        Name = p.Name,
                        Model = string.IsNullOrWhiteSpace(p.Model) ? null : p.Model,
                        ShortDescription = p.ShortDescription,
                        MainBenefit = p.MainBenefit,
                        TechnicalSpecs = p.TechnicalSpecs,
                        Condition = p.Condition,
                        Price = p.Price,
                        MonedaPrecio = string.IsNullOrWhiteSpace(p.MonedaPrecio) ? null : p.MonedaPrecio,
                        MonedasJson = p.MonedasJson,
                        TaxesShippingInstall = string.IsNullOrWhiteSpace(p.TaxesShippingInstall)
                            ? null
                            : p.TaxesShippingInstall,
                        Availability = p.Availability,
                        WarrantyReturn = p.WarrantyReturn,
                        ContentIncluded = p.ContentIncluded,
                        UsageConditions = p.UsageConditions,
                        Published = p.Published,
                        PhotoUrlsJson = p.PhotoUrlsJson,
                        CustomFieldsJson = p.CustomFieldsJson,
                        OfferQa = new List<OfferQaComment>(),
                        UpdatedAt = now,
                    });
                }

                foreach (var s in store.Services)
                {
                    if (string.IsNullOrWhiteSpace(s.Id))
                        continue;
                    db.StoreServices.Add(new StoreServiceRow
                    {
                        Id = s.Id.Trim(),
                        StoreId = sid,
                        Published = s.Published ?? true,
                        Category = s.Category,
                        TipoServicio = s.TipoServicio,
                        Descripcion = s.Descripcion,
                        Incluye = s.Incluye,
                        NoIncluye = s.NoIncluye,
                        Entregables = s.Entregables,
                        PropIntelectual = s.PropIntelectual,
                        MonedasJson = s.MonedasJson,
                        CustomFieldsJson = s.CustomFieldsJson,
                        PhotoUrlsJson = s.PhotoUrlsJson,
                        OfferQa = new List<OfferQaComment>(),
                        UpdatedAt = now,
                    });
                }
            }
        }

        AddCubaDemoContacts(db, doc.Users.Count, now);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Demo seed: cargado {Key} desde {Path} ({UserCount} usuarios).",
            doc.IdempotencyKey,
            path,
            doc.Users.Count);
    }

    /// <summary>Cada usuario demo tiene 3 contactos (otros usuarios demo) con fechas escalonadas.</summary>
    private static void AddCubaDemoContacts(AppDbContext db, int userCount, DateTimeOffset now)
    {
        var n = Math.Min(userCount, 10);
        for (var u = 1; u <= n; u++)
        {
            var ownerId = $"cuba_demo_u{u:00}";
            for (var k = 1; k <= 3; k++)
            {
                var targetIdx = ((u - 1 + k) % n) + 1;
                if (targetIdx == u)
                    targetIdx = (targetIdx % n) + 1;
                var contactId = $"cuba_demo_u{targetIdx:00}";
                var createdAt = now.AddMinutes(-(u * 11 + k * 7));
                db.UserContacts.Add(new UserContactRow
                {
                    Id = $"cuba_demo_uc_u{u:00}_t{targetIdx:00}",
                    OwnerUserId = ownerId,
                    ContactUserId = contactId,
                    CreatedAt = createdAt,
                });
            }
        }
    }
}
