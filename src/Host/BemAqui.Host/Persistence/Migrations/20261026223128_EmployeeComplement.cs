using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeComplement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_account",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_type",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bank_account_version",
                schema: "hr",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "bank_agency",
                schema: "hr",
                table: "employees",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bank_changed_at",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "bank_changed_by",
                schema: "hr",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_code",
                schema: "hr",
                table: "employees",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_holder_cpf",
                schema: "hr",
                table: "employees",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_holder_name",
                schema: "hr",
                table: "employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_mismatch_reason",
                schema: "hr",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bank_owner_mismatch",
                schema: "hr",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bank_quarantine_until",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_review_note",
                schema: "hr",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bank_reviewed_at",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "bank_reviewed_by",
                schema: "hr",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bank_self_reviewed",
                schema: "hr",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "cnh_category",
                schema: "hr",
                table: "employees",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cnh_expiry",
                schema: "hr",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cnh_number",
                schema: "hr",
                table: "employees",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_changed_at",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "hazard_pay",
                schema: "hr",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "insalubrity_pct",
                schema: "hr",
                table: "employees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "military_certificate",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_key",
                schema: "hr",
                table: "employees",
                type: "character varying(77)",
                maxLength: 77,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_key_type",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_email",
                schema: "hr",
                table: "employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "uses_transport_voucher",
                schema: "hr",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "employee_dependents",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kinship = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    ir_dependent = table.Column<bool>(type: "boolean", nullable: false),
                    family_allowance = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_dependents", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_dependents_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_cpf",
                schema: "hr",
                table: "employee_dependents",
                columns: new[] { "employee_id", "cpf" },
                unique: true,
                filter: "NOT is_deleted AND cpf IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_employee",
                schema: "hr",
                table: "employee_dependents",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_dependents",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "bank_account",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_account_type",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_account_version",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_agency",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_changed_at",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_changed_by",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_code",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_holder_cpf",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_holder_name",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_mismatch_reason",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_owner_mismatch",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_quarantine_until",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_review_note",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_reviewed_at",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_reviewed_by",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "bank_self_reviewed",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cnh_category",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cnh_expiry",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cnh_number",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "email_changed_at",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "hazard_pay",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "insalubrity_pct",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "military_certificate",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "pix_key",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "pix_key_type",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "previous_email",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "uses_transport_voucher",
                schema: "hr",
                table: "employees");
        }
    }
}
