using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChatThreadPartySoftLeaveEnsureColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repara bases donde `ChatThreadPartySoftLeave` quedó aplicada con Up() vacío:
            // `MigrateAsync` no re-ejecuta migraciones ya registradas en __EFMigrationsHistory.
            migrationBuilder.Sql(
                """
                ALTER TABLE chat_threads ADD COLUMN IF NOT EXISTS "BuyerListHiddenAtUtc" timestamp with time zone NULL;
                ALTER TABLE chat_threads ADD COLUMN IF NOT EXISTS "SellerListHiddenAtUtc" timestamp with time zone NULL;
                ALTER TABLE chat_threads ADD COLUMN IF NOT EXISTS "PartyExitedUserId" character varying(64) NULL;
                ALTER TABLE chat_threads ADD COLUMN IF NOT EXISTS "PartyExitedReason" character varying(2000) NULL;
                ALTER TABLE chat_threads ADD COLUMN IF NOT EXISTS "PartyExitedAtUtc" timestamp with time zone NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE chat_threads DROP COLUMN IF EXISTS "BuyerListHiddenAtUtc";
                ALTER TABLE chat_threads DROP COLUMN IF EXISTS "SellerListHiddenAtUtc";
                ALTER TABLE chat_threads DROP COLUMN IF EXISTS "PartyExitedUserId";
                ALTER TABLE chat_threads DROP COLUMN IF EXISTS "PartyExitedReason";
                ALTER TABLE chat_threads DROP COLUMN IF EXISTS "PartyExitedAtUtc";
                """);
        }
    }
}
