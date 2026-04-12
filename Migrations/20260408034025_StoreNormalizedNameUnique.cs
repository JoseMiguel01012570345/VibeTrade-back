using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class StoreNormalizedNameUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "stores",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // Alineado con normStoreName del cliente: trim, colapsar espacios, lower; vacío -> NULL (no entra en índice único).
            migrationBuilder.Sql(
                """
                UPDATE stores
                SET "NormalizedName" = NULLIF(
                    trim(lower(regexp_replace("Name", '\s+', ' ', 'g'))),
                    '');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stores_NormalizedName",
                table: "stores",
                column: "NormalizedName",
                unique: true,
                filter: "\"NormalizedName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stores_NormalizedName",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "stores");
        }
    }
}
