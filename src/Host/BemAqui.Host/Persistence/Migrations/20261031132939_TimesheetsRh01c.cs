using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimesheetsRh01c : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "timesheet_source",
                schema: "hr",
                table: "payroll_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "hour_bank_agreement_on",
                schema: "hr",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "timesheet_day_reviews",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timesheet_day_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_timesheet_day_reviews_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timesheets",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_minutes = table.Column<int>(type: "integer", nullable: false),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    absence_minutes = table.Column<int>(type: "integer", nullable: false),
                    late_minutes = table.Column<int>(type: "integer", nullable: false),
                    justified_minutes = table.Column<int>(type: "integer", nullable: false),
                    bank_month_minutes = table.Column<int>(type: "integer", nullable: true),
                    bank_balance_minutes = table.Column<int>(type: "integer", nullable: true),
                    worked_days = table.Column<int>(type: "integer", nullable: false),
                    absence_days = table.Column<int>(type: "integer", nullable: false),
                    justified_by_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    employee_ack_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    employee_ack_by = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_ack_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    days_snapshot = table.Column<string>(type: "text", nullable: true),
                    pdf_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timesheets", x => x.id);
                    table.ForeignKey(
                        name: "fk_timesheets_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timesheet_day_reviews_employee_day",
                schema: "hr",
                table: "timesheet_day_reviews",
                columns: new[] { "employee_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timesheets_employee_period",
                schema: "hr",
                table: "timesheets",
                columns: new[] { "employee_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timesheets_period_status",
                schema: "hr",
                table: "timesheets",
                columns: new[] { "period", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "timesheet_day_reviews",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "timesheets",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "timesheet_source",
                schema: "hr",
                table: "payroll_items");

            migrationBuilder.DropColumn(
                name: "hour_bank_agreement_on",
                schema: "hr",
                table: "employees");
        }
    }
}
