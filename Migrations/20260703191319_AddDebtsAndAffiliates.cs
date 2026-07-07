using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtsAndAffiliates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "affiliate_debts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AffiliateCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderPublicNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Liquidated = table.Column<bool>(type: "boolean", nullable: false),
                    LiquidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_affiliate_debts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "affiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommissionKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CommissionValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CommissionCurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Visits = table.Column<long>(type: "bigint", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_affiliates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_debts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CarrierUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderPublicNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RouteStopId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    TotalKm = table.Column<double>(type: "double precision", nullable: false),
                    RatePerKm = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Liquidated = table.Column<bool>(type: "boolean", nullable: false),
                    LiquidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_debts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_debts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderPublicNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Liquidated = table.Column<bool>(type: "boolean", nullable: false),
                    LiquidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_debts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_debts_AffiliateCode",
                table: "affiliate_debts",
                column: "AffiliateCode");

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_debts_Liquidated_Deleted",
                table: "affiliate_debts",
                columns: new[] { "Liquidated", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_affiliate_debts_OrderId",
                table: "affiliate_debts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_affiliates_Code",
                table: "affiliates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_affiliates_OwnerUserId",
                table: "affiliates",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_debts_CarrierUserId",
                table: "carrier_debts",
                column: "CarrierUserId");

            migrationBuilder.CreateIndex(
                name: "IX_carrier_debts_Liquidated_Deleted",
                table: "carrier_debts",
                columns: new[] { "Liquidated", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_carrier_debts_OrderId",
                table: "carrier_debts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_debts_Liquidated_Deleted",
                table: "warehouse_debts",
                columns: new[] { "Liquidated", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_debts_OrderId",
                table: "warehouse_debts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_debts_StoreId",
                table: "warehouse_debts",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affiliate_debts");

            migrationBuilder.DropTable(
                name: "affiliates");

            migrationBuilder.DropTable(
                name: "carrier_debts");

            migrationBuilder.DropTable(
                name: "warehouse_debts");
        }
    }
}
