using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalculatedPayrollFlh02a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "calculated_at",
                schema: "hr",
                table: "payslips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calculation_issues",
                schema: "hr",
                table: "payslips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calculator_version",
                schema: "hr",
                table: "payslips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "employer_charges",
                schema: "hr",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "employer_charges_memory",
                schema: "hr",
                table: "payslips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inputs_snapshot",
                schema: "hr",
                table: "payslips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "parameter_ids",
                schema: "hr",
                table: "payslips",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "tax_table_ids",
                schema: "hr",
                table: "payslips",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<bool>(
                name: "calculated",
                schema: "hr",
                table: "payslip_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "memory",
                schema: "hr",
                table: "payslip_lines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "hr",
                table: "payslip_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "calculated_at",
                schema: "hr",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "calculated_by",
                schema: "hr",
                table: "payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "calculating_since",
                schema: "hr",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calculator_version",
                schema: "hr",
                table: "payroll_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_simulation",
                schema: "hr",
                table: "payroll_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "rectifies_run_id",
                schema: "hr",
                table: "payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "calculated",
                schema: "hr",
                table: "payroll_run_charges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "calculation",
                schema: "hr",
                table: "payroll_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "deducts_irrf_base",
                schema: "hr",
                table: "payroll_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "family_allowance_docs_ok",
                schema: "hr",
                table: "employee_dependents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "payroll_calculation_logs",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: true),
                    calculator_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_table_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    parameter_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    employees = table.Column<int>(type: "integer", nullable: false),
                    gross_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    net_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    gross_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_calculation_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_logs_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type", "mode" },
                unique: true,
                filter: "rectifies_run_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_rectifies_run_id",
                schema: "hr",
                table: "payroll_runs",
                column: "rectifies_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_calculation_logs_run",
                schema: "hr",
                table: "payroll_calculation_logs",
                columns: new[] { "payroll_run_id", "at" });

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_runs_payroll_runs_rectifies_run_id",
                schema: "hr",
                table: "payroll_runs",
                column: "rectifies_run_id",
                principalSchema: "hr",
                principalTable: "payroll_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payroll_runs_payroll_runs_rectifies_run_id",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropTable(
                name: "payroll_calculation_logs",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_rectifies_run_id",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculated_at",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "calculation_issues",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "calculator_version",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "employer_charges",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "employer_charges_memory",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "inputs_snapshot",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "parameter_ids",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "tax_table_ids",
                schema: "hr",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "calculated",
                schema: "hr",
                table: "payslip_lines");

            migrationBuilder.DropColumn(
                name: "memory",
                schema: "hr",
                table: "payslip_lines");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "hr",
                table: "payslip_lines");

            migrationBuilder.DropColumn(
                name: "calculated_at",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculated_by",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculating_since",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculator_version",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "is_simulation",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "rectifies_run_id",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculated",
                schema: "hr",
                table: "payroll_run_charges");

            migrationBuilder.DropColumn(
                name: "calculation",
                schema: "hr",
                table: "payroll_items");

            migrationBuilder.DropColumn(
                name: "deducts_irrf_base",
                schema: "hr",
                table: "payroll_items");

            migrationBuilder.DropColumn(
                name: "family_allowance_docs_ok",
                schema: "hr",
                table: "employee_dependents");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type" },
                unique: true);
        }
    }
}
