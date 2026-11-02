using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VacationsFlh03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs");

            migrationBuilder.CreateTable(
                name: "vacation_periods",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    absences = table.Column<int>(type: "integer", nullable: false),
                    entitled_days = table.Column<int>(type: "integer", nullable: false),
                    taken_days = table.Column<int>(type: "integer", nullable: false),
                    sold_days = table.Column<int>(type: "integer", nullable: false),
                    concession_ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restarted_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    restarted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacation_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_vacation_periods_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vacation_schedules",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    sold_days = table.Column<int>(type: "integer", nullable: false),
                    advance13th = table.Column<bool>(type: "boolean", nullable: false),
                    payment_due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    calculated_split = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacation_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_vacation_schedules_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vacation_schedules_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vacation_schedules_vacation_periods_period_id",
                        column: x => x.period_id,
                        principalSchema: "hr",
                        principalTable: "vacation_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_period_type",
                schema: "hr",
                table: "payroll_runs",
                columns: new[] { "period", "type", "mode" },
                unique: true,
                filter: "rectifies_run_id IS NULL AND NOT (type = 'Vacation' AND mode = 'Calculated')");

            migrationBuilder.CreateIndex(
                name: "ix_vacation_periods_employee_sequence",
                schema: "hr",
                table: "vacation_periods",
                columns: new[] { "employee_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vacation_periods_status_concession",
                schema: "hr",
                table: "vacation_periods",
                columns: new[] { "status", "concession_ends_on" });

            migrationBuilder.CreateIndex(
                name: "ix_vacation_schedules_employee_start",
                schema: "hr",
                table: "vacation_schedules",
                columns: new[] { "employee_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_vacation_schedules_period",
                schema: "hr",
                table: "vacation_schedules",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_vacation_schedules_run",
                schema: "hr",
                table: "vacation_schedules",
                column: "payroll_run_id",
                unique: true,
                filter: "payroll_run_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vacation_schedules",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "vacation_periods",
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
                filter: "rectifies_run_id IS NULL");
        }
    }
}
