using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeConnectAndSellerPayoutTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeConnectedAccountId",
                table: "user_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPayoutStripeTransferId",
                table: "agreement_service_payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeConnectedAccountId",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "SellerPayoutStripeTransferId",
                table: "agreement_service_payments");
        }
    }
}
