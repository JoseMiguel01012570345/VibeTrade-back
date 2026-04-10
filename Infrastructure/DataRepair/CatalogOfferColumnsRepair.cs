using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Infrastructure.DataRepair;

/// <summary>
/// Añade columnas de ofertas (Q&amp;A, fotos de servicio) si faltan. Idempotente para entornos sin migraciones versionadas en repo.
/// </summary>
public static class CatalogOfferColumnsRepair
{
    public static async Task RunAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE store_products ADD COLUMN IF NOT EXISTS "OfferQaJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE store_services ADD COLUMN IF NOT EXISTS "OfferQaJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE store_services ADD COLUMN IF NOT EXISTS "PhotoUrlsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            """,
            cancellationToken);
    }
}
