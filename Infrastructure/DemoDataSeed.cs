using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Infrastructure;

/// <summary>
/// Datos de demostración: cuentas, tiendas con ubicación y catálogo (productos/servicios).
/// Idempotente: no vuelve a insertar si ya existen las cuentas demo.
/// </summary>
public static class DemoDataSeed
{
    private const int TargetDemoAccounts = 10;
    private const string DemoUserIdPrefix = "demo_seed_user_";
    private const string DemoUserIdLikePattern = DemoUserIdPrefix + "%";

    public static async Task RunIfNeededAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("DemoSeed:Enabled", false))
            return;

        // No usar StartsWith(..., StringComparison): EF Core no lo traduce a SQL en PostgreSQL.
        var existingUsers = await db.UserAccounts
            .CountAsync(u => EF.Functions.Like(u.Id, DemoUserIdLikePattern), cancellationToken);
        if (existingUsers >= TargetDemoAccounts)
        {
            var demoContactCount = await db.UserContacts.CountAsync(
                c => EF.Functions.Like(c.Id, "demo_uc_%"),
                cancellationToken);
            if (demoContactCount >= TargetDemoAccounts * 3)
            {
                logger.LogInformation(
                    "Demo seed: omitido (ya existen cuentas y contactos demo).");
                return;
            }

            if (demoContactCount == 0)
            {
                var t = DateTimeOffset.UtcNow;
                AddDemoUserContacts(db, t);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Demo seed: contactos demo añadidos entre usuarios demo ya existentes.");
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        var joinedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var categories = CatalogCategories.ProductAndService;

        for (var u = 1; u <= TargetDemoAccounts; u++)
        {
            var userId = $"{DemoUserIdPrefix}{u:00}";
            var phoneDigits = (54_900_000_000L + u).ToString(CultureInfo.InvariantCulture);

            db.UserAccounts.Add(new UserAccount
            {
                Id = userId,
                PhoneDigits = phoneDigits,
                DisplayName = $"Usuario demo {u:00}",
                PhoneDisplay = $"+54 9 11 {u:0000}-{u:0000}",
                SavedOfferIdsJson = "[]",
                TrustScore = 48 + u % 15,
                CreatedAt = now,
                UpdatedAt = now,
            });

            var storeCountRng = new Random(u * 7919);
            var storeCount = storeCountRng.Next(2, 4);

            for (var s = 1; s <= storeCount; s++)
            {
                var storeId = $"demo_seed_store_u{u:00}_s{s}";
                var storeName = $"Tienda demo {u:00}-{s}";
                var norm = MarketStoreNameNormalizer.Normalize(storeName);

                db.Stores.Add(new StoreRow
                {
                    Id = storeId,
                    OwnerUserId = userId,
                    Name = storeName,
                    NormalizedName = norm,
                    Verified = (u + s) % 3 != 0,
                    TransportIncluded = (u + s) % 2 == 0,
                    TrustScore = 72 + (u * 3 + s * 5) % 25,
                    CategoriesJson = """["Alimentos","Mercancías","Servicios"]""",
                    Pitch = $"Oferta de ejemplo para {storeName}. Envíos y consultas según disponibilidad.",
                    JoinedAtMs = joinedMs,
                    LocationLatitude = -34.6037 + u * 0.011 + s * 0.005,
                    LocationLongitude = -58.3816 + u * 0.009 + s * 0.004,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                var itemRng = new Random(u * 10_007 + s * 97);
                var itemCount = itemRng.Next(5, 11);

                for (var k = 0; k < itemCount; k++)
                {
                    var cat = categories[itemRng.Next(categories.Count)];
                    if (itemRng.Next(2) == 0)
                        AddDemoProduct(db, storeId, cat, itemRng, now);
                    else
                        AddDemoService(db, storeId, cat, itemRng, now);
                }
            }
        }

        AddDemoUserContacts(db, now);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Demo seed: creadas {Count} cuentas con tiendas (2–3 c/u), ítems por tienda y contactos entre usuarios demo.",
            TargetDemoAccounts);
    }

    /// <summary>Cada usuario demo tiene 3 contactos (otros usuarios demo) con fechas escalonadas.</summary>
    private static void AddDemoUserContacts(AppDbContext db, DateTimeOffset now)
    {
        for (var u = 1; u <= TargetDemoAccounts; u++)
        {
            var ownerId = $"{DemoUserIdPrefix}{u:00}";
            for (var k = 1; k <= 3; k++)
            {
                var targetIdx = ((u - 1 + k) % TargetDemoAccounts) + 1;
                if (targetIdx == u)
                    targetIdx = (targetIdx % TargetDemoAccounts) + 1;
                var contactId = $"{DemoUserIdPrefix}{targetIdx:00}";
                var createdAt = now.AddMinutes(-(u * 11 + k * 7));
                db.UserContacts.Add(new UserContactRow
                {
                    Id = $"demo_uc_u{u:00}_t{targetIdx:00}",
                    OwnerUserId = ownerId,
                    ContactUserId = contactId,
                    CreatedAt = createdAt,
                });
            }
        }
    }

    private static void AddDemoProduct(
        AppDbContext db,
        string storeId,
        string category,
        Random rnd,
        DateTimeOffset now)
    {
        var n = rnd.Next(1, 500);
        var id = "demo_p_" + Guid.NewGuid().ToString("N")[..16];
        db.StoreProducts.Add(new StoreProductRow
        {
            Id = id,
            StoreId = storeId,
            Category = category,
            Name = $"Producto demo #{n}",
            Model = $"MD-{n:000}",
            ShortDescription = "Artículo de demostración generado al iniciar el backend.",
            MainBenefit = "Listo para probar búsqueda y detalle de tienda.",
            TechnicalSpecs = "Especificaciones de ejemplo.",
            Condition = rnd.Next(2) == 0 ? "Nuevo" : "Usado",
            Price = (rnd.Next(5, 500) * 100).ToString(CultureInfo.InvariantCulture),
            MonedaPrecio = "USD",
            MonedasJson = """["USD","EUR"]""",
            Availability = "Stock demo",
            WarrantyReturn = "Consultar política de la tienda.",
            ContentIncluded = "Unidad principal.",
            UsageConditions = "Uso conforme a normativa local.",
            Published = true,
            PhotoUrlsJson = "[]",
            CustomFieldsJson = "[]",
            OfferQa = new List<OfferQaComment>(),
            UpdatedAt = now,
        });
    }

    private static void AddDemoService(
        AppDbContext db,
        string storeId,
        string category,
        Random rnd,
        DateTimeOffset now)
    {
        var n = rnd.Next(1, 500);
        var id = "demo_svc_" + Guid.NewGuid().ToString("N")[..14];
        db.StoreServices.Add(new StoreServiceRow
        {
            Id = id,
            StoreId = storeId,
            Published = true,
            Category = category,
            TipoServicio = $"Servicio tipo {n % 5 + 1}",
            Descripcion = "Servicio de demostración generado al iniciar el backend.",
            Incluye = "Sesión inicial y diagnóstico.",
            NoIncluye = "Materiales no listados expresamente.",
            Entregables = "Informe resumido.",
            PropIntelectual = "Según acuerdo entre partes.",
            MonedasJson = """["USD"]""",
            CustomFieldsJson = "[]",
            PhotoUrlsJson = "[]",
            OfferQa = new List<OfferQaComment>(),
            UpdatedAt = now,
        });
    }
}
