using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class RouteTramoSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_tramo_subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StopId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StopOrden = table.Column<int>(type: "integer", nullable: false),
                    CarrierUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TransportServiceLabel = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_tramo_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_route_tramo_subscriptions_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_route_tramo_subscriptions_ThreadId",
                table: "route_tramo_subscriptions",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_route_tramo_subscriptions_ThreadId_RouteSheetId_StopId_Carr~",
                table: "route_tramo_subscriptions",
                columns: new[] { "ThreadId", "RouteSheetId", "StopId", "CarrierUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_tramo_subscriptions");
        }
    }
}
