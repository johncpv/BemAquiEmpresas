using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminationsFlh05 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.CreateTable(
                name: "terminations",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notice_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notice_days = table.Column<int>(type: "integer", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: false),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fgts_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    fgts_statement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminations", x => x.id);
                    table.ForeignKey(
                        name: "fk_terminations_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_terminations_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type", "mode" },
                unique: true,
                filter: "rectifies_run_id IS NULL AND NOT (type IN ('Vacation', 'Termination') AND mode = 'Calculated')");

            migrationBuilder.CreateIndex(
                name: "ix_terminations_employee_active",
                schema: "hr",
                table: "terminations",
                column: "employee_id",
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_terminations_run",
                schema: "hr",
                table: "terminations",
                column: "payroll_run_id",
                unique: true,
                filter: "payroll_run_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_terminations_status_date",
                schema: "hr",
                table: "terminations",
                columns: new[] { "status", "termination_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terminations",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type", "mode" },
                unique: true,
                filter: "rectifies_run_id IS NULL AND NOT (type = 'Vacation' AND mode = 'Calculated')");
        }
    }
}
