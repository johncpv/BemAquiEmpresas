using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollEntered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parameters",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_items",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inss_base = table.Column<bool>(type: "boolean", nullable: false),
                    fgts_base = table.Column<bool>(type: "boolean", nullable: false),
                    irrf_base = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_runs",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_run_charges",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payee_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_run_charges_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payslips",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    inss_base = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fgts_base = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    irrf_base = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fgts_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslips", x => x.id);
                    table.ForeignKey(
                        name: "fk_payslips_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payslips_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payslip_lines",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payslip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_payslip_lines_payroll_items_payroll_item_id",
                        column: x => x.payroll_item_id,
                        principalSchema: "hr",
                        principalTable: "payroll_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payslip_lines_payslips_payslip_id",
                        column: x => x.payslip_id,
                        principalSchema: "hr",
                        principalTable: "payslips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_parameters_key",
                schema: "hr",
                table: "parameters",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_items_code",
                schema: "hr",
                table: "payroll_items",
                column: "code",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_items_seed_key",
                schema: "hr",
                table: "payroll_items",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_charges_run",
                schema: "hr",
                table: "payroll_run_charges",
                column: "payroll_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payslip_lines_item",
                schema: "hr",
                table: "payslip_lines",
                column: "payroll_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_lines_payslip",
                schema: "hr",
                table: "payslip_lines",
                column: "payslip_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslips_employee",
                schema: "hr",
                table: "payslips",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslips_run_employee",
                schema: "hr",
                table: "payslips",
                columns: new[] { "payroll_run_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parameters",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_run_charges",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payslip_lines",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_items",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payslips",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_runs",
                schema: "hr");
        }
    }
}
