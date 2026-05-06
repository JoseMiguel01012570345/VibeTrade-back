using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuthPendingOtpsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_pending_otps",
                columns: table => new
                {
                    PhoneDigits = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CodeLength = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_pending_otps", x => x.PhoneDigits);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_pending_otps_ExpiresAt",
                table: "auth_pending_otps",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_pending_otps");
        }
    }
}
