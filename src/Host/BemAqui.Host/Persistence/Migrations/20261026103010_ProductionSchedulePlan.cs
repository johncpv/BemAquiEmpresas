using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionSchedulePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_allocations",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_allocations", x => x.id);
                    table.CheckConstraint("ck_schedule_allocations_hours", "hours > 0");
                });

            migrationBuilder.CreateTable(
                name: "schedule_runs",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    orders_count = table.Column<int>(type: "integer", nullable: false),
                    steps_count = table.Column<int>(type: "integer", nullable: false),
                    changed_orders_count = table.Column<int>(type: "integer", nullable: false),
                    without_forecast_count = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_step_forecasts",
                schema: "production",
                columns: table => new
                {
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    no_forecast_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_step_forecasts", x => x.step_id);
                    table.CheckConstraint("ck_schedule_step_forecasts_dates", "planned_end IS NULL OR planned_end >= planned_start");
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_allocations_date_work_center_id",
                schema: "production",
                table: "schedule_allocations",
                columns: new[] { "date", "work_center_id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_allocations_production_order_id",
                schema: "production",
                table: "schedule_allocations",
                column: "production_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_allocations_step_id",
                schema: "production",
                table: "schedule_allocations",
                column: "step_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_runs_finished_at",
                schema: "production",
                table: "schedule_runs",
                column: "finished_at");

            migrationBuilder.CreateIndex(
                name: "ux_schedule_runs_pending",
                schema: "production",
                table: "schedule_runs",
                column: "status",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_step_forecasts_production_order_id",
                schema: "production",
                table: "schedule_step_forecasts",
                column: "production_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_allocations",
                schema: "production");

            migrationBuilder.DropTable(
                name: "schedule_runs",
                schema: "production");

            migrationBuilder.DropTable(
                name: "schedule_step_forecasts",
                schema: "production");
        }
    }
}
