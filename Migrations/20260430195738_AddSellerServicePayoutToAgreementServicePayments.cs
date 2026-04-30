using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeTrade.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerServicePayoutToAgreementServicePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SellerPayoutCardBrandSnapshot",
                table: "agreement_service_payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPayoutCardLast4Snapshot",
                table: "agreement_service_payments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPayoutPaymentMethodStripeId",
                table: "agreement_service_payments",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SellerPayoutRecordedAtUtc",
                table: "agreement_service_payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SellerPayoutCardBrandSnapshot",
                table: "agreement_service_payments");

            migrationBuilder.DropColumn(
                name: "SellerPayoutCardLast4Snapshot",
                table: "agreement_service_payments");

            migrationBuilder.DropColumn(
                name: "SellerPayoutPaymentMethodStripeId",
                table: "agreement_service_payments");

            migrationBuilder.DropColumn(
                name: "SellerPayoutRecordedAtUtc",
                table: "agreement_service_payments");
        }
    }
}
