using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRolesJsonData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La migración anterior añadió RolesJson con default de cadena JSON ("") que no es un array;
            // normaliza filas existentes a un array vacío válido y corrige el default de la columna.
            migrationBuilder.Sql(
                @"UPDATE user_accounts SET ""RolesJson"" = '[]'::jsonb WHERE jsonb_typeof(""RolesJson"") IS DISTINCT FROM 'array';");
            migrationBuilder.Sql(
                @"ALTER TABLE user_accounts ALTER COLUMN ""RolesJson"" SET DEFAULT '[]'::jsonb;");

            // Defensa por si alguna columna List<string> heredó el mismo default inválido.
            migrationBuilder.Sql(
                @"UPDATE user_accounts SET ""SavedOfferIdsJson"" = '[]'::jsonb WHERE jsonb_typeof(""SavedOfferIdsJson"") IS DISTINCT FROM 'array';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Migración de datos: sin reversión.
        }
    }
}
