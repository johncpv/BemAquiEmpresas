using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningProjectForecasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_forecasts",
                schema: "commercial",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fabrication_end = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reason_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_forecasts", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_project_forecasts_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_project_forecasts",
                schema: "production",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fabrication_end = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reason_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_project_forecasts", x => x.project_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_forecasts",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "schedule_project_forecasts",
                schema: "production");
        }
    }
}
