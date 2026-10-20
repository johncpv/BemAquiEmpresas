using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancePayables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payables",
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
                    retention_iss = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retention_inss = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retention_ir = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retention_pcc = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_estimate = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recurring_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competence = table.Column<DateOnly>(type: "date", nullable: true),
                    installment_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    payment_details = table.Column<string>(type: "jsonb", nullable: true),
                    payment_method_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_expenses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    payment_details = table.Column<string>(type: "jsonb", nullable: true),
                    due_day = table.Column<int>(type: "integer", nullable: false),
                    start = table.Column<DateOnly>(type: "date", nullable: false),
                    end = table.Column<DateOnly>(type: "date", nullable: true),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_expenses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payables_installment_group_id",
                schema: "finance",
                table: "payables",
                column: "installment_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_number",
                schema: "finance",
                table: "payables",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payables_open_due",
                schema: "finance",
                table: "payables",
                columns: new[] { "status", "due_date" },
                filter: "status NOT IN ('Paid','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin",
                schema: "finance",
                table: "payables",
                columns: new[] { "origin", "origin_id" },
                unique: true,
                filter: "origin_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payables_party_id",
                schema: "finance",
                table: "payables",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_recurring_competence",
                schema: "finance",
                table: "payables",
                columns: new[] { "recurring_expense_id", "competence" },
                unique: true,
                filter: "recurring_expense_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payables",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "recurring_expenses",
                schema: "finance");
        }
    }
}
