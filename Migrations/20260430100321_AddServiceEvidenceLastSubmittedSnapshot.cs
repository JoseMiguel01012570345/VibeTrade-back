using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceEvidenceLastSubmittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSubmittedAtUtc",
                table: "service_evidences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSubmittedAttachmentsJson",
                table: "service_evidences",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastSubmittedText",
                table: "service_evidences",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSubmittedAtUtc",
                table: "service_evidences");

            migrationBuilder.DropColumn(
                name: "LastSubmittedAttachmentsJson",
                table: "service_evidences");

            migrationBuilder.DropColumn(
                name: "LastSubmittedText",
                table: "service_evidences");
        }
    }
}
