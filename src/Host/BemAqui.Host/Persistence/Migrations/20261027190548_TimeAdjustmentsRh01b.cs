using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimeAdjustmentsRh01b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "adjustment_id",
                schema: "hr",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "hr",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "void_adjustment_id",
                schema: "hr",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                schema: "hr",
                table: "time_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by",
                schema: "hr",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "hr",
                table: "time_entries",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "time_adjustments",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    absence_minutes = table.Column<int>(type: "integer", nullable: true),
                    absence_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    justification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    created_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_adjustments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_employee_day",
                schema: "hr",
                table: "time_adjustments",
                columns: new[] { "employee_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_entry",
                schema: "hr",
                table: "time_adjustments",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_status_requested",
                schema: "hr",
                table: "time_adjustments",
                columns: new[] { "status", "requested_at" });

            // RH-01b CA 7: o tipo de afastamento da CAD-04c1 vira o motivo da lista única de ausências, sem perder dado (Doença →
            // Atestado médico; Outros → Falta justificada; os demais ficam iguais). A volta (Down) desfaz só esses dois.
            migrationBuilder.Sql("UPDATE hr.employee_history SET leave_type = 'MedicalCertificate' WHERE leave_type = 'Illness';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET leave_type = 'JustifiedAbsence' WHERE leave_type = 'Other';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET previous_leave_type = 'MedicalCertificate' WHERE previous_leave_type = 'Illness';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET previous_leave_type = 'JustifiedAbsence' WHERE previous_leave_type = 'Other';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE hr.employee_history SET leave_type = 'Illness' WHERE leave_type = 'MedicalCertificate';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET leave_type = 'Other' WHERE leave_type = 'JustifiedAbsence';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET previous_leave_type = 'Illness' WHERE previous_leave_type = 'MedicalCertificate';");
            migrationBuilder.Sql("UPDATE hr.employee_history SET previous_leave_type = 'Other' WHERE previous_leave_type = 'JustifiedAbsence';");

            migrationBuilder.DropTable(
                name: "time_adjustments",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "adjustment_id",
                schema: "hr",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "hr",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "void_adjustment_id",
                schema: "hr",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "hr",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "voided_by",
                schema: "hr",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "hr",
                table: "time_entries");
        }
    }
}
