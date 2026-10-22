using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionLaborEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "awaiting_approval_since",
                schema: "production",
                table: "production_order_steps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "self_approved",
                schema: "production",
                table: "production_order_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "labor_entries",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paused_minutes = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf = table.Column<bool>(type: "boolean", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    close_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labor_entries", x => x.id);
                    table.CheckConstraint("ck_labor_entries_interval", "ended_at IS NULL OR ended_at >= started_at");
                    table.CheckConstraint("ck_labor_entries_paused", "paused_minutes >= 0");
                    table.ForeignKey(
                        name: "fk_labor_entries_production_order_steps_order_step_id",
                        column: x => x.order_step_id,
                        principalSchema: "production",
                        principalTable: "production_order_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_labor_entries_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "step_pauses",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    labor_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notes = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_pauses", x => x.id);
                    table.CheckConstraint("ck_step_pauses_interval", "ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "fk_step_pauses_labor_entries_labor_entry_id",
                        column: x => x.labor_entry_id,
                        principalSchema: "production",
                        principalTable: "labor_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_labor_entries_employee_id_started_at",
                schema: "production",
                table: "labor_entries",
                columns: new[] { "employee_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_labor_entries_order_id",
                schema: "production",
                table: "labor_entries",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_labor_entries_order_step_id",
                schema: "production",
                table: "labor_entries",
                column: "order_step_id");

            migrationBuilder.CreateIndex(
                name: "ux_labor_entries_open_employee",
                schema: "production",
                table: "labor_entries",
                column: "employee_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_step_pauses_order_id",
                schema: "production",
                table: "step_pauses",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_step_pauses_open_entry",
                schema: "production",
                table: "step_pauses",
                column: "labor_entry_id",
                unique: true,
                filter: "ended_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "step_pauses",
                schema: "production");

            migrationBuilder.DropTable(
                name: "labor_entries",
                schema: "production");

            migrationBuilder.DropColumn(
                name: "awaiting_approval_since",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "self_approved",
                schema: "production",
                table: "production_order_steps");
        }
    }
}
