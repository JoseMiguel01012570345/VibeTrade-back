using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteCatalogRouteSheetsContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_contacts_OwnerUserId_ContactUserId",
                table: "user_contacts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "user_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "store_services",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "store_products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "chat_route_sheets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "chat_route_sheets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_contacts_OwnerUserId_ContactUserId",
                table: "user_contacts",
                columns: new[] { "OwnerUserId", "ContactUserId" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_contacts_OwnerUserId_ContactUserId",
                table: "user_contacts");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "user_contacts");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "store_services");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "chat_route_sheets");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "chat_route_sheets");

            migrationBuilder.CreateIndex(
                name: "IX_user_contacts_OwnerUserId_ContactUserId",
                table: "user_contacts",
                columns: new[] { "OwnerUserId", "ContactUserId" },
                unique: true);
        }
    }
}
