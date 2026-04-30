using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AgreementPaymentIdempotencyPerAgreement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agreement_currency_payments_ClientIdempotencyKey",
                table: "agreement_currency_payments");

            migrationBuilder.CreateIndex(
                name: "IX_agpay_agreement_idempotency",
                table: "agreement_currency_payments",
                columns: new[] { "TradeAgreementId", "ClientIdempotencyKey" },
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agpay_agreement_idempotency",
                table: "agreement_currency_payments");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_currency_payments_ClientIdempotencyKey",
                table: "agreement_currency_payments",
                column: "ClientIdempotencyKey",
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL");
        }
    }
}
