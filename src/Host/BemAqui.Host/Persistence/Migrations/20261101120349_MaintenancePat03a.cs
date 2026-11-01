using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenancePat03a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_meter_readings",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_on = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_meter_readings", x => x.id);
                    table.CheckConstraint("ck_asset_meter_readings_value", "value >= 0");
                    table.ForeignKey(
                        name: "fk_asset_meter_readings_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    frequency_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    frequency_value = table.Column<int>(type: "integer", nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    default_supplier_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checklist = table.Column<string>(type: "jsonb", nullable: false),
                    last_done_at = table.Column<DateOnly>(type: "date", nullable: true),
                    last_done_meter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    next_due_at = table.Column<DateOnly>(type: "date", nullable: true),
                    next_due_meter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plans", x => x.id);
                    table.CheckConstraint("ck_maintenance_plans_frequency_value", "frequency_value > 0");
                    table.CheckConstraint("ck_maintenance_plans_lead_days", "lead_days >= 0");
                    table.ForeignKey(
                        name: "fk_maintenance_plans_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_orders",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    problem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    machine_stopped = table.Column<bool>(type: "boolean", nullable: false),
                    scheduled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by = table.Column<Guid>(type: "uuid", nullable: true),
                    work_done = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_orders", x => x.id);
                    table.CheckConstraint("ck_maintenance_orders_status", "status IN ('Open', 'InProgress', 'Done', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_maintenance_orders_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_orders_maintenance_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "assets",
                        principalTable: "maintenance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_order_checklist",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    marked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_order_checklist", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_order_checklist_maintenance_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "assets",
                        principalTable: "maintenance_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_meter_readings_asset_id_read_on",
                schema: "assets",
                table: "asset_meter_readings",
                columns: new[] { "asset_id", "read_on", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_order_checklist_order_id_sequence",
                schema: "assets",
                table: "maintenance_order_checklist",
                columns: new[] { "order_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_asset_id_created_at",
                schema: "assets",
                table: "maintenance_orders",
                columns: new[] { "asset_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_number",
                schema: "assets",
                table: "maintenance_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_status",
                schema: "assets",
                table: "maintenance_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_supplier_party_id",
                schema: "assets",
                table: "maintenance_orders",
                column: "supplier_party_id",
                filter: "supplier_party_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_orders_plan_due_key",
                schema: "assets",
                table: "maintenance_orders",
                columns: new[] { "plan_id", "due_key" },
                unique: true,
                filter: "due_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_asset_id",
                schema: "assets",
                table: "maintenance_plans",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_default_supplier_party_id",
                schema: "assets",
                table: "maintenance_plans",
                column: "default_supplier_party_id",
                filter: "default_supplier_party_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_next_due_at",
                schema: "assets",
                table: "maintenance_plans",
                column: "next_due_at",
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_meter_readings",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "maintenance_order_checklist",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "maintenance_orders",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "maintenance_plans",
                schema: "assets");
        }
    }
}
