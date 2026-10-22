using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLeaveChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "hr",
                table: "employee_history",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "leave_change",
                schema: "hr",
                table: "employee_history",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_id",
                schema: "hr",
                table: "employee_history",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_leave_type",
                schema: "hr",
                table: "employee_history",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_history_leave_changes",
                schema: "hr",
                table: "employee_history",
                column: "leave_id",
                filter: "leave_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_history_leave_changes",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "leave_change",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "leave_id",
                schema: "hr",
                table: "employee_history");

            migrationBuilder.DropColumn(
                name: "previous_leave_type",
                schema: "hr",
                table: "employee_history");
        }
    }
}
