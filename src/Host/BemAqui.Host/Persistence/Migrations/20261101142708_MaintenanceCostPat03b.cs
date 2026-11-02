using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceCostPat03b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at",
                schema: "assets",
                table: "parameters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by",
                schema: "assets",
                table: "parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "contract_value",
                schema: "assets",
                table: "maintenance_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cost",
                schema: "assets",
                table: "maintenance_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "downtime_from",
                schema: "assets",
                table: "maintenance_orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "downtime_revision",
                schema: "assets",
                table: "maintenance_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "downtime_to",
                schema: "assets",
                table: "maintenance_orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_hours",
                schema: "assets",
                table: "maintenance_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hours_per_day",
                schema: "assets",
                table: "maintenance_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payable_id",
                schema: "assets",
                table: "maintenance_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "downtimes",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_end_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    open_ended = table.Column<bool>(type: "boolean", nullable: false),
                    hours_per_day = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_downtimes", x => x.id);
                    table.CheckConstraint("ck_downtimes_hours_per_day", "hours_per_day > 0");
                    table.CheckConstraint("ck_downtimes_period", "planned_end_on >= starts_on");
                });

            migrationBuilder.CreateTable(
                name: "maintenance_order_costs",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    hourly_cost_snapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_order_costs", x => x.id);
                    table.CheckConstraint("ck_maintenance_order_costs_amount", "amount >= 0");
                    table.CheckConstraint("ck_maintenance_order_costs_cost_type", "cost_type IN ('Labor', 'Service', 'Parts', 'Other')");
                    table.ForeignKey(
                        name: "fk_maintenance_order_costs_maintenance_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "assets",
                        principalTable: "maintenance_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_done_at",
                schema: "assets",
                table: "maintenance_orders",
                column: "done_at",
                filter: "status = 'Done'");

            migrationBuilder.CreateIndex(
                name: "ix_downtimes_work_center_id_starts_on",
                schema: "production",
                table: "downtimes",
                columns: new[] { "work_center_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ux_downtimes_source",
                schema: "production",
                table: "downtimes",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_order_costs_employee_id",
                schema: "assets",
                table: "maintenance_order_costs",
                column: "employee_id",
                filter: "employee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_order_costs_order_id",
                schema: "assets",
                table: "maintenance_order_costs",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_order_costs_payable_id",
                schema: "assets",
                table: "maintenance_order_costs",
                column: "payable_id",
                unique: true,
                filter: "payable_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "downtimes",
                schema: "production");

            migrationBuilder.DropTable(
                name: "maintenance_order_costs",
                schema: "assets");

            migrationBuilder.DropIndex(
                name: "ix_maintenance_orders_done_at",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                schema: "assets",
                table: "parameters");

            migrationBuilder.DropColumn(
                name: "confirmed_by",
                schema: "assets",
                table: "parameters");

            migrationBuilder.DropColumn(
                name: "contract_value",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "cost",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "downtime_from",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "downtime_revision",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "downtime_to",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "expected_hours",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "hours_per_day",
                schema: "assets",
                table: "maintenance_orders");

            migrationBuilder.DropColumn(
                name: "payable_id",
                schema: "assets",
                table: "maintenance_orders");
        }
    }
}
