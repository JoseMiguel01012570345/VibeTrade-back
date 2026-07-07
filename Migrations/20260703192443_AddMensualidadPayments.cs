using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMensualidadPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mensualidad_payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TrustScoreBefore = table.Column<int>(type: "integer", nullable: false),
                    TrustScoreAfter = table.Column<int>(type: "integer", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensualidad_payments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mensualidad_payments_UserId_PaidAtUtc",
                table: "mensualidad_payments",
                columns: new[] { "UserId", "PaidAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensualidad_payments");
        }
    }
}
