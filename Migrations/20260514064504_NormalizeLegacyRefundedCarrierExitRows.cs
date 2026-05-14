using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeLegacyRefundedCarrierExitRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE route_stop_deliveries
                SET "State" = 'refunded'
                WHERE "State" = 'refunded_carrier_exit';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Estado eliminado del producto; no se revierte.
        }
    }
}
