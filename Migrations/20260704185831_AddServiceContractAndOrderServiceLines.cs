using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceContractAndOrderServiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "store_services",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "FixedPrice",
                table: "store_services",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceDay",
                table: "store_services",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceMonth",
                table: "store_services",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "LineKind",
                table: "order_lines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "product");

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceDay",
                table: "order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceMonth",
                table: "order_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceId",
                table: "order_lines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTipo",
                table: "order_lines",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_ServiceId",
                table: "order_lines",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_lines_ServiceId",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "FixedPrice",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "RecurrenceDay",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "RecurrenceMonth",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "LineKind",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "RecurrenceDay",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "RecurrenceMonth",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "ServiceTipo",
                table: "order_lines");
        }
    }
}
