using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParallelPayrollFlh06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parallel_runs",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    run_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payroll_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculated_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    compared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    compared_by = table.Column<Guid>(type: "uuid", nullable: true),
                    differences = table.Column<int>(type: "integer", nullable: false),
                    employees = table.Column<int>(type: "integer", nullable: false),
                    entered_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    calculated_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    accepted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accountant_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    accountant_crc = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    signed_term_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parallel_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_parallel_runs_payroll_runs_calculated_run_id",
                        column: x => x.calculated_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parallel_runs_payroll_runs_entered_run_id",
                        column: x => x.entered_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_item_mappings",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    calculated_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_item_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_mode_changes",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_period = table.Column<DateOnly>(type: "date", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parallel_run_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    accountant_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    accountant_crc = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_mode_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parallel_differences",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parallel_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_item_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    item_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    calculated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    difference_pct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    justification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    classified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correction_case = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parallel_differences", x => x.id);
                    table.ForeignKey(
                        name: "fk_parallel_differences_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_parallel_differences_payroll_parallel_runs_parallel_run_id",
                        column: x => x.parallel_run_id,
                        principalSchema: "hr",
                        principalTable: "parallel_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parallel_differences_employee",
                schema: "hr",
                table: "parallel_differences",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_parallel_differences_row",
                schema: "hr",
                table: "parallel_differences",
                columns: new[] { "parallel_run_id", "employee_id", "payroll_item_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parallel_runs_calculated_run_id",
                schema: "hr",
                table: "parallel_runs",
                column: "calculated_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_parallel_runs_entered_run_id",
                schema: "hr",
                table: "parallel_runs",
                column: "entered_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_parallel_runs_period_type",
                schema: "hr",
                table: "parallel_runs",
                columns: new[] { "period", "payroll_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_item_mappings_entered",
                schema: "hr",
                table: "payroll_item_mappings",
                column: "entered_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_mode_changes_type",
                schema: "hr",
                table: "payroll_mode_changes",
                columns: new[] { "run_type", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parallel_differences",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_item_mappings",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_mode_changes",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "parallel_runs",
                schema: "hr");
        }
    }
}
