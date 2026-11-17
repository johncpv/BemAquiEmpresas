using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentMethodsPag01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquirer_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nfe_payment_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    allows_change = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_methods_bank_accounts_destination_account_id",
                        column: x => x.destination_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acquirer_fees",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquirer_party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    installments = table.Column<int>(type: "integer", nullable: false),
                    fee_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    settlement_days = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acquirer_fees", x => x.id);
                    table.ForeignKey(
                        name: "fk_acquirer_fees_payment_methods_payment_method_id",
                        column: x => x.payment_method_id,
                        principalSchema: "finance",
                        principalTable: "payment_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acquirer_fees_payment_method_id_acquirer_party_id_brand_ins",
                schema: "finance",
                table: "acquirer_fees",
                columns: new[] { "payment_method_id", "acquirer_party_id", "brand", "installments", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_acquirer_party_id",
                schema: "finance",
                table: "payment_methods",
                column: "acquirer_party_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_destination_account_id",
                schema: "finance",
                table: "payment_methods",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_seed_key",
                schema: "finance",
                table: "payment_methods",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquirer_fees",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "finance");
        }
    }
}
