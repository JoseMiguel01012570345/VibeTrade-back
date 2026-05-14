using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class UserContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_contacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContactUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_contacts_user_accounts_ContactUserId",
                        column: x => x.ContactUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_contacts_user_accounts_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_contacts_ContactUserId",
                table: "user_contacts",
                column: "ContactUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_contacts_OwnerUserId",
                table: "user_contacts",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_contacts_OwnerUserId_ContactUserId",
                table: "user_contacts",
                columns: new[] { "OwnerUserId", "ContactUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_contacts");
        }
    }
}
