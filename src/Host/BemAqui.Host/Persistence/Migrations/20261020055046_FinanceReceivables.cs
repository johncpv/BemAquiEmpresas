using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_financial_transactions_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parameters",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installment_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receivable_settlements",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    backdate_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reversal_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_receivable_settlements_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_bank_account_id_date",
                schema: "finance",
                table: "financial_transactions",
                columns: new[] { "bank_account_id", "date" })
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_receivable_id",
                schema: "finance",
                table: "financial_transactions",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_parameters_key",
                schema: "finance",
                table: "parameters",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receivable_settlements_date",
                schema: "finance",
                table: "receivable_settlements",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_settlements_receivable_id",
                schema: "finance",
                table: "receivable_settlements",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_installment_group_id",
                schema: "finance",
                table: "receivables",
                column: "installment_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_number",
                schema: "finance",
                table: "receivables",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receivables_open_due",
                schema: "finance",
                table: "receivables",
                columns: new[] { "status", "due_date" },
                filter: "status NOT IN ('Paid','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_origin_origin_id",
                schema: "finance",
                table: "receivables",
                columns: new[] { "origin", "origin_id" });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_party_id",
                schema: "finance",
                table: "receivables",
                column: "party_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "parameters",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivable_settlements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivables",
                schema: "finance");
        }
    }
}
