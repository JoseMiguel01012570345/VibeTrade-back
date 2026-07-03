using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersPedidoAndStoreStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricePerKm",
                table: "stores",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PricePerKmCurrencyCode",
                table: "stores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "store_products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitsSold",
                table: "store_products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BuyerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SellerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerFirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CustomerLastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PhonePrimary = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhoneSecondary = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "text", nullable: false),
                    DeliveryLatitude = table.Column<double>(type: "double precision", nullable: true),
                    DeliveryLongitude = table.Column<double>(type: "double precision", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DeliveryFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PricePerKmSnapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RouteDistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    PaymentStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    PaymentHeldAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentRefundedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClientEvidenceDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClientEvidenceUrlsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ClientEvidenceNote = table.Column<string>(type: "text", nullable: true),
                    ClientEvidenceSubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClientEvidenceDecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClientEvidenceRejectReason = table.Column<string>(type: "text", nullable: true),
                    RouteSheetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AffiliateCodeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AffiliateCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    WarehouseDebtsRecorded = table.Column<bool>(type: "boolean", nullable: false),
                    AffiliateDebtRecorded = table.Column<bool>(type: "boolean", nullable: false),
                    CarrierDebtRecorded = table.Column<bool>(type: "boolean", nullable: false),
                    IsInvalidated = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvalidatedReason = table.Column<string>(type: "text", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TechnicalSpecs = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_lines_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_OrderId",
                table: "order_lines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_ProductId",
                table: "order_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_BuyerUserId",
                table: "orders",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_PublicNumber",
                table: "orders",
                column: "PublicNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status",
                table: "orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_orders_StoreId",
                table: "orders",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_lines");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropColumn(
                name: "PricePerKm",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "PricePerKmCurrencyCode",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "UnitsSold",
                table: "store_products");
        }
    }
}
