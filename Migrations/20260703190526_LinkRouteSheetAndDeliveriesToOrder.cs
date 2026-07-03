using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class LinkRouteSheetAndDeliveriesToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderId",
                table: "route_stop_deliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderId",
                table: "chat_route_sheets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stop_deliveries_OrderId",
                table: "route_stop_deliveries",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_route_sheets_OrderId",
                table: "chat_route_sheets",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_route_stop_deliveries_OrderId",
                table: "route_stop_deliveries");

            migrationBuilder.DropIndex(
                name: "IX_chat_route_sheets_OrderId",
                table: "chat_route_sheets");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "route_stop_deliveries");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "chat_route_sheets");
        }
    }
}
