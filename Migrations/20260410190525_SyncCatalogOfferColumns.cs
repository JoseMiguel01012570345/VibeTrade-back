using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SyncCatalogOfferColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS: compatible with DBs that already ran CatalogOfferColumnsRepair before this migration shipped.
            migrationBuilder.Sql(
                """
                ALTER TABLE store_products ADD COLUMN IF NOT EXISTS "OfferQaJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
                ALTER TABLE store_services ADD COLUMN IF NOT EXISTS "OfferQaJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
                ALTER TABLE store_services ADD COLUMN IF NOT EXISTS "PhotoUrlsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE store_products DROP COLUMN IF EXISTS "OfferQaJson";
                ALTER TABLE store_services DROP COLUMN IF EXISTS "OfferQaJson";
                ALTER TABLE store_services DROP COLUMN IF EXISTS "PhotoUrlsJson";
                """);
        }
    }
}
