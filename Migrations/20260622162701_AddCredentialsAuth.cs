using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialsAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAt",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "user_accounts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhoneVerifiedAt",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "user_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_pending_email_otps",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_pending_email_otps", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "auth_pending_password_resets",
                columns: table => new
                {
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NewPasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_pending_password_resets", x => x.Email);
                });

            migrationBuilder.CreateTable(
                name: "auth_pending_registrations",
                columns: table => new
                {
                    RegistrationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PhoneDigits = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneDisplay = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_pending_registrations", x => x.RegistrationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_Email",
                table: "user_accounts",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_Username",
                table: "user_accounts",
                column: "Username",
                unique: true,
                filter: "\"Username\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_pending_email_otps_ExpiresAt",
                table: "auth_pending_email_otps",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_auth_pending_password_resets_ExpiresAt",
                table: "auth_pending_password_resets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_auth_pending_registrations_ExpiresAt",
                table: "auth_pending_registrations",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_pending_email_otps");

            migrationBuilder.DropTable(
                name: "auth_pending_password_resets");

            migrationBuilder.DropTable(
                name: "auth_pending_registrations");

            migrationBuilder.DropIndex(
                name: "IX_user_accounts_Email",
                table: "user_accounts");

            migrationBuilder.DropIndex(
                name: "IX_user_accounts_Username",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "PhoneVerifiedAt",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "user_accounts");
        }
    }
}
