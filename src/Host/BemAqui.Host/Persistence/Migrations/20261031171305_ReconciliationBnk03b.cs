using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationBnk03b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "reconciled",
                schema: "finance",
                table: "financial_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "statement_line_id",
                schema: "finance",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "financial_transactions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_closings",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    until_date = table.Column<DateOnly>(type: "date", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    bank_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    system_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_reconciliation_closings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_reconciliation_closings_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_reconciliations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    closed_period_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    undone_by = table.Column<Guid>(type: "uuid", nullable: true),
                    undo_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_reconciliations", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_reconciliations_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_rules",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    keyword = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    line_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    entry_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_links",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_reconciliation_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_reconciliation_links_bank_reconciliations_reconciliati",
                        column: x => x.reconciliation_id,
                        principalSchema: "finance",
                        principalTable: "bank_reconciliations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_reconciliation_links_bank_statement_lines_statement_li",
                        column: x => x.statement_line_id,
                        principalSchema: "finance",
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_reconciliation_links_financial_transactions_transactio",
                        column: x => x.transaction_id,
                        principalSchema: "finance",
                        principalTable: "financial_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_statement_line_id",
                schema: "finance",
                table: "financial_transactions",
                column: "statement_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_reconciliation_closings_bank_account_id_until_date",
                schema: "finance",
                table: "bank_reconciliation_closings",
                columns: new[] { "bank_account_id", "until_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_reconciliation_links_reconciliation_id",
                schema: "finance",
                table: "bank_reconciliation_links",
                column: "reconciliation_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_reconciliation_links_statement_line_id",
                schema: "finance",
                table: "bank_reconciliation_links",
                column: "statement_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_reconciliation_links_transaction_id",
                schema: "finance",
                table: "bank_reconciliation_links",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_reconciliations_bank_account_id_created_at",
                schema: "finance",
                table: "bank_reconciliations",
                columns: new[] { "bank_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_rules_is_active",
                schema: "finance",
                table: "bank_statement_rules",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_reconciliation_closings",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_reconciliation_links",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_statement_rules",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_reconciliations",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_financial_transactions_statement_line_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "reconciled",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "statement_line_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "financial_transactions");
        }
    }
}
