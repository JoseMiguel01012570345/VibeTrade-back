using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RolesJson",
                table: "user_accounts",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "analytics_page_views",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_page_views", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_view_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_view_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytics_page_views_Path",
                table: "analytics_page_views",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_page_views_SessionKey",
                table: "analytics_page_views",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_page_views_ViewedAt",
                table: "analytics_page_views",
                column: "ViewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_sessions_FirstSeenAt",
                table: "analytics_sessions",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_sessions_SessionKey",
                table: "analytics_sessions",
                column: "SessionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_view_events_ProductId",
                table: "product_view_events",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_view_events_ViewedAt",
                table: "product_view_events",
                column: "ViewedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_page_views");

            migrationBuilder.DropTable(
                name: "analytics_sessions");

            migrationBuilder.DropTable(
                name: "product_view_events");

            migrationBuilder.DropColumn(
                name: "RolesJson",
                table: "user_accounts");
        }
    }
}
