using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteBackgroundJobsAndCalculations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_background_jobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JobType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_background_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "route_sheet_route_calculations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatrixJson = table.Column<string>(type: "jsonb", nullable: true),
                    VisitOrderJson = table.Column<string>(type: "jsonb", nullable: true),
                    TotalKm = table.Column<double>(type: "double precision", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_sheet_route_calculations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_route_background_jobs_Status_CreatedAtUtc",
                table: "route_background_jobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_route_background_jobs_ThreadId_RouteSheetId",
                table: "route_background_jobs",
                columns: new[] { "ThreadId", "RouteSheetId" });

            migrationBuilder.CreateIndex(
                name: "IX_route_sheet_route_calculations_ThreadId_RouteSheetId",
                table: "route_sheet_route_calculations",
                columns: new[] { "ThreadId", "RouteSheetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_background_jobs");

            migrationBuilder.DropTable(
                name: "route_sheet_route_calculations");
        }
    }
}
