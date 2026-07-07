using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class DropAgreementMerchandise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchandise_evidences");

            migrationBuilder.DropTable(
                name: "trade_agreement_merchandise_lines");

            migrationBuilder.DropTable(
                name: "trade_agreement_merchandise_metas");

            migrationBuilder.DropTable(
                name: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "IncludeMerchandise",
                table: "trade_agreements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeMerchandise",
                table: "trade_agreements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "agreement_merchandise_line_paids",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementCurrencyPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    BuyerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MerchandiseLineId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_merchandise_line_paids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_merchandise_line_paids_agreement_currency_payment~",
                        column: x => x.AgreementCurrencyPaymentId,
                        principalTable: "agreement_currency_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_merchandise_lines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Cantidad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Descuento = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DevolucionPlazos = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionQuienPaga = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionesDesc = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impuestos = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LinkedStoreProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Moneda = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Regulaciones = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TipoEmbalaje = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ValorUnitario = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_merchandise_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_merchandise_lines_trade_agreements_TradeAgr~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_merchandise_metas",
                columns: table => new
                {
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DevolucionPlazos = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionQuienPaga = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionesDesc = table.Column<string>(type: "text", nullable: false),
                    Moneda = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Regulaciones = table.Column<string>(type: "text", nullable: false),
                    TipoEmbalaje = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_merchandise_metas", x => x.TradeAgreementId);
                    table.ForeignKey(
                        name: "FK_trade_agreement_merchandise_metas_trade_agreements_TradeAgr~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merchandise_evidences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementMerchandiseLinePaidId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    BuyerDecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSubmittedAttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSubmittedText = table.Column<string>(type: "text", nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchandise_evidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_merchandise_evidences_agreement_merchandise_line_paids_Agre~",
                        column: x => x.AgreementMerchandiseLinePaidId,
                        principalTable: "agreement_merchandise_line_paids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_merchandise_line_paids_AgreementCurrencyPaymentId",
                table: "agreement_merchandise_line_paids",
                column: "AgreementCurrencyPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_merchandise_line_paids_MerchandiseLineId_Currency",
                table: "agreement_merchandise_line_paids",
                columns: new[] { "MerchandiseLineId", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_merchandise_line_paids_ThreadId_Status",
                table: "agreement_merchandise_line_paids",
                columns: new[] { "ThreadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_merchandise_evidences_AgreementMerchandiseLinePaidId",
                table: "merchandise_evidences",
                column: "AgreementMerchandiseLinePaidId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_merchandise_lines_TradeAgreementId",
                table: "trade_agreement_merchandise_lines",
                column: "TradeAgreementId");
        }
    }
}
