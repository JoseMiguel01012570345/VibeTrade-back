using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class RelationalCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhoneDigits = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    PhoneDisplay = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Instagram = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telegram = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    XAccount = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TrustScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    TransportIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    TrustScore = table.Column<int>(type: "integer", nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CategoriesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Pitch = table.Column<string>(type: "text", nullable: false),
                    JoinedAtMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stores_user_accounts_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_products",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    ShortDescription = table.Column<string>(type: "text", nullable: false),
                    MainBenefit = table.Column<string>(type: "text", nullable: false),
                    TechnicalSpecs = table.Column<string>(type: "text", nullable: false),
                    Condition = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<string>(type: "text", nullable: false),
                    TaxesShippingInstall = table.Column<string>(type: "text", nullable: true),
                    Availability = table.Column<string>(type: "text", nullable: false),
                    WarrantyReturn = table.Column<string>(type: "text", nullable: false),
                    ContentIncluded = table.Column<string>(type: "text", nullable: false),
                    UsageConditions = table.Column<string>(type: "text", nullable: false),
                    PhotoUrlsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_products_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_services",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    TipoServicio = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    RiesgosJson = table.Column<string>(type: "jsonb", nullable: false),
                    Incluye = table.Column<string>(type: "text", nullable: false),
                    NoIncluye = table.Column<string>(type: "text", nullable: false),
                    DependenciasJson = table.Column<string>(type: "jsonb", nullable: false),
                    Entregables = table.Column<string>(type: "text", nullable: false),
                    GarantiasJson = table.Column<string>(type: "jsonb", nullable: false),
                    PropIntelectual = table.Column<string>(type: "text", nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_services_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_store_products_StoreId",
                table: "store_products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_store_services_StoreId",
                table: "store_services",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_stores_OwnerUserId",
                table: "stores",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_PhoneDigits",
                table: "user_accounts",
                column: "PhoneDigits",
                unique: true,
                filter: "\"PhoneDigits\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_products");

            migrationBuilder.DropTable(
                name: "store_services");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "user_accounts");
        }
    }
}
