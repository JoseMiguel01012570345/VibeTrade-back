using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AgreementCurrencyPaymentsAndRouteLegPaids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_currency_payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuyerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtotalAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    ClimateAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    StripeFeeAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentMethodStripeId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    StripeErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ClientIdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClientSecretForConfirmation = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_currency_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_currency_payments_trade_agreements_TradeAgreement~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agreement_route_leg_paids",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementCurrencyPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_route_leg_paids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_route_leg_paids_agreement_currency_payments_Agree~",
                        column: x => x.AgreementCurrencyPaymentId,
                        principalTable: "agreement_currency_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_currency_payments_ClientIdempotencyKey",
                table: "agreement_currency_payments",
                column: "ClientIdempotencyKey",
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_currency_payments_TradeAgreementId_ThreadId",
                table: "agreement_currency_payments",
                columns: new[] { "TradeAgreementId", "ThreadId" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_route_leg_paids_AgreementCurrencyPaymentId",
                table: "agreement_route_leg_paids",
                column: "AgreementCurrencyPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_route_leg_paids_RouteSheetId_RouteStopId",
                table: "agreement_route_leg_paids",
                columns: new[] { "RouteSheetId", "RouteStopId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_route_leg_paids");

            migrationBuilder.DropTable(
                name: "agreement_currency_payments");
        }
    }
}
