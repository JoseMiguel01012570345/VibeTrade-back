using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchandiseHeldAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerUserId",
                table: "agreement_merchandise_line_paids",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "agreement_merchandise_line_paids",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedAtUtc",
                table: "agreement_merchandise_line_paids",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "agreement_merchandise_line_paids",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ThreadId",
                table: "agreement_merchandise_line_paids",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TradeAgreementId",
                table: "agreement_merchandise_line_paids",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE agreement_merchandise_line_paids AS ml
                SET "TradeAgreementId" = cp."TradeAgreementId",
                    "ThreadId" = cp."ThreadId",
                    "BuyerUserId" = cp."BuyerUserId",
                    "CreatedAtUtc" = COALESCE(cp."CreatedAtUtc", NOW() AT TIME ZONE 'utc'),
                    "Status" = 'held'
                FROM agreement_currency_payments AS cp
                WHERE ml."AgreementCurrencyPaymentId" = cp."Id";
                """);

            migrationBuilder.CreateTable(
                name: "merchandise_evidences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementMerchandiseLinePaidId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSubmittedText = table.Column<string>(type: "text", nullable: false),
                    LastSubmittedAttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BuyerDecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "IX_agreement_merchandise_line_paids_ThreadId_Status",
                table: "agreement_merchandise_line_paids",
                columns: new[] { "ThreadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_merchandise_evidences_AgreementMerchandiseLinePaidId",
                table: "merchandise_evidences",
                column: "AgreementMerchandiseLinePaidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchandise_evidences");

            migrationBuilder.DropIndex(
                name: "IX_agreement_merchandise_line_paids_ThreadId_Status",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "BuyerUserId",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "agreement_merchandise_line_paids");

            migrationBuilder.DropColumn(
                name: "TradeAgreementId",
                table: "agreement_merchandise_line_paids");
        }
    }
}
