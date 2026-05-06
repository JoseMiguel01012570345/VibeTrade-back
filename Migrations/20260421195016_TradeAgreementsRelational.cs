using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class TradeAgreementsRelational : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LegacyServiceBlockJson",
                table: "trade_agreements");

            migrationBuilder.DropColumn(
                name: "MerchandiseJson",
                table: "trade_agreements");

            migrationBuilder.DropColumn(
                name: "MerchandiseMetaJson",
                table: "trade_agreements");

            migrationBuilder.DropColumn(
                name: "ServicesJson",
                table: "trade_agreements");

            migrationBuilder.CreateTable(
                name: "trade_agreement_merchandise_lines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    LinkedStoreProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Tipo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Cantidad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValorUnitario = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Descuento = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Impuestos = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TipoEmbalaje = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionesDesc = table.Column<string>(type: "text", nullable: false),
                    DevolucionQuienPaga = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionPlazos = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Regulaciones = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_merchandise_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_merchandise_lines_trade_agreements_TradeAgr~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_merchandise_metas",
                columns: table => new
                {
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TipoEmbalaje = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionesDesc = table.Column<string>(type: "text", nullable: false),
                    DevolucionQuienPaga = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DevolucionPlazos = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Regulaciones = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_merchandise_metas", x => x.TradeAgreementId);
                    table.ForeignKey(
                        name: "FK_trade_agreement_merchandise_metas_trade_agreements_TradeAgr~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TradeAgreementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    LinkedStoreServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Configured = table.Column<bool>(type: "boolean", nullable: false),
                    TipoServicio = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TiempoStartDate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TiempoEndDate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Incluye = table.Column<string>(type: "text", nullable: false),
                    NoIncluye = table.Column<string>(type: "text", nullable: false),
                    Entregables = table.Column<string>(type: "text", nullable: false),
                    MetodoPago = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MedicionCumplimiento = table.Column<string>(type: "text", nullable: false),
                    PenalIncumplimiento = table.Column<string>(type: "text", nullable: false),
                    NivelResponsabilidad = table.Column<string>(type: "text", nullable: false),
                    PropIntelectual = table.Column<string>(type: "text", nullable: false),
                    ScheduleCalendarYear = table.Column<int>(type: "integer", nullable: false),
                    ScheduleDefaultWindowStart = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScheduleDefaultWindowEnd = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RiesgosEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DependenciasEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GarantiasEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GarantiasTexto = table.Column<string>(type: "text", nullable: false),
                    PenalAtrasoEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PenalAtrasoTexto = table.Column<string>(type: "text", nullable: false),
                    TerminacionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TerminacionAvisoDias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_items_trade_agreements_TradeAgreeme~",
                        column: x => x.TradeAgreementId,
                        principalTable: "trade_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_dependencias",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_dependencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_dependencias_trade_agreement_servic~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_monedas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_monedas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_monedas_trade_agreement_service_ite~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_payment_entries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_payment_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_payment_entries_trade_agreement_ser~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_payment_months",
                columns: table => new
                {
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_payment_months", x => new { x.ServiceItemId, x.Month });
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_payment_months_trade_agreement_serv~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_riesgos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_riesgos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_riesgos_trade_agreement_service_ite~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_schedule_days",
                columns: table => new
                {
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    CalendarDay = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_schedule_days", x => new { x.ServiceItemId, x.Month, x.CalendarDay });
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_schedule_days_trade_agreement_servi~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_schedule_months",
                columns: table => new
                {
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_schedule_months", x => new { x.ServiceItemId, x.Month });
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_schedule_months_trade_agreement_ser~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_schedule_overrides",
                columns: table => new
                {
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    CalendarDay = table.Column<int>(type: "integer", nullable: false),
                    WindowStart = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WindowEnd = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_schedule_overrides", x => new { x.ServiceItemId, x.Month, x.CalendarDay });
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_schedule_overrides_trade_agreement_~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_agreement_service_terminacion_causas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServiceItemId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_agreement_service_terminacion_causas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_agreement_service_terminacion_causas_trade_agreement_~",
                        column: x => x.ServiceItemId,
                        principalTable: "trade_agreement_service_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_merchandise_lines_TradeAgreementId",
                table: "trade_agreement_merchandise_lines",
                column: "TradeAgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_dependencias_ServiceItemId",
                table: "trade_agreement_service_dependencias",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_items_TradeAgreementId",
                table: "trade_agreement_service_items",
                column: "TradeAgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_monedas_ServiceItemId",
                table: "trade_agreement_service_monedas",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_payment_entries_ServiceItemId",
                table: "trade_agreement_service_payment_entries",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_riesgos_ServiceItemId",
                table: "trade_agreement_service_riesgos",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_trade_agreement_service_terminacion_causas_ServiceItemId",
                table: "trade_agreement_service_terminacion_causas",
                column: "ServiceItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_agreement_merchandise_lines");

            migrationBuilder.DropTable(
                name: "trade_agreement_merchandise_metas");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_dependencias");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_monedas");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_payment_entries");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_payment_months");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_riesgos");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_schedule_days");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_schedule_months");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_schedule_overrides");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_terminacion_causas");

            migrationBuilder.DropTable(
                name: "trade_agreement_service_items");

            migrationBuilder.AddColumn<string>(
                name: "LegacyServiceBlockJson",
                table: "trade_agreements",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchandiseJson",
                table: "trade_agreements",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MerchandiseMetaJson",
                table: "trade_agreements",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicesJson",
                table: "trade_agreements",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }
    }
}
