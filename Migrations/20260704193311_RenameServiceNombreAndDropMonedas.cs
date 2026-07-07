using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class RenameServiceNombreAndDropMonedas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonedasJson",
                table: "store_services");

            migrationBuilder.RenameColumn(
                name: "TipoServicio",
                table: "store_services",
                newName: "NombreServicio");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NombreServicio",
                table: "store_services",
                newName: "TipoServicio");

            migrationBuilder.AddColumn<string>(
                name: "MonedasJson",
                table: "store_services",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }
    }
}
