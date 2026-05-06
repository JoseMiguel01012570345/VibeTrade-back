using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductMonedaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MonedaPrecio",
                table: "store_products",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MonedasJson",
                table: "store_products",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonedaPrecio",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "MonedasJson",
                table: "store_products");
        }
    }
}
