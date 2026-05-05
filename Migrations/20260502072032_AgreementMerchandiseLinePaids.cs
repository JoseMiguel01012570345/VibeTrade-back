using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AgreementMerchandiseLinePaids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_merchandise_line_paids",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementCurrencyPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MerchandiseLineId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_agreement_merchandise_line_paids_AgreementCurrencyPaymentId",
                table: "agreement_merchandise_line_paids",
                column: "AgreementCurrencyPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_agreement_merchandise_line_paids_MerchandiseLineId_Currency",
                table: "agreement_merchandise_line_paids",
                columns: new[] { "MerchandiseLineId", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_merchandise_line_paids");
        }
    }
}
