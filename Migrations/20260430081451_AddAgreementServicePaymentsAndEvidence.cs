using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementServicePaymentsAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_service_payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuyerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntryMonth = table.Column<int>(type: "integer", nullable: false),
                    EntryDay = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgreementCurrencyPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_service_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_service_payments_agreement_currency_payments_Agre~",
                        column: x => x.AgreementCurrencyPaymentId,
                        principalTable: "agreement_currency_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_agreement_service_payments_trade_agreements_TradeAgreementId",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_evidences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementServicePaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BuyerDecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_evidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_evidences_agreement_service_payments_AgreementServi~",
                        column: x => x.AgreementServicePaymentId,
                        principalTable: "agreement_service_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_service_payments_AgreementCurrencyPaymentId",
                table: "agreement_service_payments",
                column: "AgreementCurrencyPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_service_payments_TradeAgreementId_ThreadId",
                table: "agreement_service_payments",
                columns: new[] { "TradeAgreementId", "ThreadId" });

            migrationBuilder.CreateIndex(
                name: "IX_agsp_unique_installment",
                table: "agreement_service_payments",
                columns: new[] { "TradeAgreementId", "ServiceItemId", "EntryMonth", "EntryDay", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_evidences_AgreementServicePaymentId",
                table: "service_evidences",
                column: "AgreementServicePaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_evidences");

            migrationBuilder.DropTable(
                name: "agreement_service_payments");
        }
    }
}
