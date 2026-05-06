using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuthSessionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.Token);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_ExpiresAt",
                table: "auth_sessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions");
        }
    }
}
