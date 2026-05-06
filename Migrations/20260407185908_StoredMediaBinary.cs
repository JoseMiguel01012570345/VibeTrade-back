using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class StoredMediaBinary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_media",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_media", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_media");
        }
    }
}
