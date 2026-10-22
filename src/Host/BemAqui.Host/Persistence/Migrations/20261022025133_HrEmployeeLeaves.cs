using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrEmployeeLeaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "hr",
                table: "employees",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "end_published_at",
                schema: "hr",
                table: "employee_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "leave_end_date",
                schema: "hr",
                table: "employee_history",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "leave_type",
                schema: "hr",
                table: "employee_history",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "start_published_at",
                schema: "hr",
                table: "employee_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_history_leave_pending",
                schema: "hr",
                table: "employee_history",
                column: "effective_date",
                filter: "kind = 'Leave' AND (start_published_at IS NULL OR end_published_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_history_leave_pending",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "end_published_at",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "leave_end_date",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "leave_type",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "start_published_at",
                schema: "hr",
                table: "employee_history");
        }
    }
}
