using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeAgreementExtraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_agreement_extra_fields",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ValueKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TextValue = table.Column<string>(type: "text", nullable: true),
                    MediaUrl = table.Column<string>(type: "text", nullable: true),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_extra_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_extra_fields_trade_agreements_TradeAgreemen~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_extra_fields_TradeAgreementId",
                table: "trade_agreement_extra_fields",
                column: "TradeAgreementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_agreement_extra_fields");
        }
    }
}
