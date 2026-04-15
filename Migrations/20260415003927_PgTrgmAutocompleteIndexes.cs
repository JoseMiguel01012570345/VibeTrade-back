using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class PgTrgmAutocompleteIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_store_services_TipoServicio_trgm" ON store_services USING gin ("TipoServicio" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_store_services_Category_trgm" ON store_services USING gin ("Category" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_store_products_Name_trgm" ON store_products USING gin ("Name" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_store_products_Model_trgm" ON store_products USING gin ("Model" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_stores_Name_trgm" ON stores USING gin ("Name" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_stores_NormalizedName_trgm" ON stores USING gin ("NormalizedName" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_stores_NormalizedName_trgm";
                DROP INDEX IF EXISTS "IX_stores_Name_trgm";
                DROP INDEX IF EXISTS "IX_store_products_Model_trgm";
                DROP INDEX IF EXISTS "IX_store_products_Name_trgm";
                DROP INDEX IF EXISTS "IX_store_services_Category_trgm";
                DROP INDEX IF EXISTS "IX_store_services_TipoServicio_trgm";
                """);
        }
    }
}
