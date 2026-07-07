using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreInventoryHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryIdsJson",
                table: "store_products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PendingApproval",
                table: "store_products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupplierId",
                table: "store_products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "store_banners",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    MediaUrl = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_banners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_banners_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ParentCategoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_categories_store_categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "store_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_store_categories_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_suppliers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PortalUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    PlatformDebtAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    PlatformDebtCurrencyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "USD"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_suppliers_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_store_products_SupplierId",
                table: "store_products",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_store_banners_StoreId_Kind_Active",
                table: "store_banners",
                columns: new[] { "StoreId", "Kind", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_store_categories_ParentCategoryId",
                table: "store_categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_store_categories_StoreId",
                table: "store_categories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_store_categories_StoreId_Name",
                table: "store_categories",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_store_suppliers_PortalUsername",
                table: "store_suppliers",
                column: "PortalUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_suppliers_StoreId",
                table: "store_suppliers",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_store_products_store_suppliers_SupplierId",
                table: "store_products",
                column: "SupplierId",
                principalTable: "store_suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_store_products_store_suppliers_SupplierId",
                table: "store_products");

            migrationBuilder.DropTable(
                name: "store_banners");

            migrationBuilder.DropTable(
                name: "store_categories");

            migrationBuilder.DropTable(
                name: "store_suppliers");

            migrationBuilder.DropIndex(
                name: "IX_store_products_SupplierId",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "CategoryIdsJson",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "PendingApproval",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "store_products");
        }
    }
}
