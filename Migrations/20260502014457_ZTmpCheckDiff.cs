using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ZTmpCheckDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carrier_delivery_evidences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CarrierUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSubmittedText = table.Column<string>(type: "text", nullable: false),
                    LastSubmittedAttachmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeadlineAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_delivery_evidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_delivery_evidences_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carrier_ownership_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CarrierUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_ownership_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_ownership_events_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carrier_telemetry_samples",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CarrierUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    SpeedKmh = table.Column<double>(type: "double precision", nullable: true),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServerReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProgressFraction = table.Column<double>(type: "double precision", nullable: true),
                    OffRoute = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_telemetry_samples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carrier_telemetry_samples_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "route_stop_deliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CurrentOwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnershipGrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidenceDeadlineAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefundedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefundEligibleReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RefundEligibleSinceUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTelemetryProgressFraction = table.Column<double>(type: "double precision", nullable: true),
                    ProximityNotifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_stop_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_route_stop_deliveries_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_delivery_evidences_ThreadId_TradeAgreementId_RouteSheetId_RouteStopId",
                table: "carrier_delivery_evidences",
                columns: new[] { "ThreadId", "TradeAgreementId", "RouteSheetId", "RouteStopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_carrier_ownership_events_RouteSheetId_RouteStopId_AtUtc",
                table: "carrier_ownership_events",
                columns: new[] { "RouteSheetId", "RouteStopId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_ownership_events_ThreadId_AtUtc",
                table: "carrier_ownership_events",
                columns: new[] { "ThreadId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_telemetry_samples_RouteStopId_ReportedAtUtc",
                table: "carrier_telemetry_samples",
                columns: new[] { "RouteStopId", "ReportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_telemetry_samples_ThreadId",
                table: "carrier_telemetry_samples",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_route_stop_deliveries_CurrentOwnerUserId",
                table: "route_stop_deliveries",
                column: "CurrentOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_route_stop_deliveries_ThreadId_TradeAgreementId_RouteSheetId_RouteStopId",
                table: "route_stop_deliveries",
                columns: new[] { "ThreadId", "TradeAgreementId", "RouteSheetId", "RouteStopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stop_deliveries_ThreadId_State",
                table: "route_stop_deliveries",
                columns: new[] { "ThreadId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_delivery_evidences");

            migrationBuilder.DropTable(
                name: "carrier_ownership_events");

            migrationBuilder.DropTable(
                name: "carrier_telemetry_samples");

            migrationBuilder.DropTable(
                name: "route_stop_deliveries");
        }
    }
}
