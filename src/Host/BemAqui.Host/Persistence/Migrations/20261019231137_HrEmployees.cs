using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "employee_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    union_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admission_date = table.Column<DateOnly>(type: "date", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    rg = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pis = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    ctps_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ctps_series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    voter_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_job_positions_position_id",
                        column: x => x.position_id,
                        principalSchema: "hr",
                        principalTable: "job_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_labor_unions_union_id",
                        column: x => x.union_id,
                        principalSchema: "hr",
                        principalTable: "unions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_work_schedules_work_schedule_id",
                        column: x => x.work_schedule_id,
                        principalSchema: "hr",
                        principalTable: "work_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_history",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    old_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    new_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_history_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_employee_id",
                schema: "identity",
                table: "users",
                column: "employee_id",
                unique: true,
                filter: "employee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_history_employee",
                schema: "hr",
                table: "employee_history",
                columns: new[] { "employee_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_cpf",
                schema: "hr",
                table: "employees",
                column: "cpf",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employees_position_id",
                schema: "hr",
                table: "employees",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_registration",
                schema: "hr",
                table: "employees",
                column: "registration",
                unique: true,
                filter: "NOT is_deleted AND registration IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employees_union_id",
                schema: "hr",
                table: "employees",
                column: "union_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_work_schedule_id",
                schema: "hr",
                table: "employees",
                column: "work_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_history",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_users_employee_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "employee_id",
                schema: "identity",
                table: "users");
        }
    }
}
