using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bank_entry_id",
                schema: "finance",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_id",
                schema: "finance",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bank_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    backdate_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_transfers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    backdate_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_bank_entry_id",
                schema: "finance",
                table: "financial_transactions",
                column: "bank_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_transfer_id",
                schema: "finance",
                table: "financial_transactions",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_entries_bank_account_id",
                schema: "finance",
                table: "bank_entries",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_from_account_id",
                schema: "finance",
                table: "bank_transfers",
                column: "from_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_to_account_id",
                schema: "finance",
                table: "bank_transfers",
                column: "to_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_transfers",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_financial_transactions_bank_entry_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropIndex(
                name: "ix_financial_transactions_transfer_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "bank_entry_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "transfer_id",
                schema: "finance",
                table: "financial_transactions");
        }
    }
}
