using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectCostsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cost",
                schema: "production",
                table: "labor_entries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "cost_backfilled",
                schema: "production",
                table: "labor_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "cost_missing",
                schema: "production",
                table: "labor_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "cost_position_id",
                schema: "production",
                table: "labor_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cost_version",
                schema: "production",
                table: "labor_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_cost_snapshot",
                schema: "production",
                table: "labor_entries",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "labor_cost_history",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    labor_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_version = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    hourly_cost_snapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    previous_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_missing = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labor_cost_history", x => x.id);
                    table.CheckConstraint("ck_labor_cost_history_version", "cost_version >= 1");
                    table.ForeignKey(
                        name: "fk_labor_cost_history_labor_entries_labor_entry_id",
                        column: x => x.labor_entry_id,
                        principalSchema: "production",
                        principalTable: "labor_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_costs",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_costs", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_costs_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_labor_entries_cost_missing",
                schema: "production",
                table: "labor_entries",
                column: "cost_position_id",
                filter: "cost_missing");

            migrationBuilder.CreateIndex(
                name: "ix_labor_cost_history_labor_entry_id_cost_version",
                schema: "production",
                table: "labor_cost_history",
                columns: new[] { "labor_entry_id", "cost_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_labor_cost_history_order_id",
                schema: "production",
                table: "labor_cost_history",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_costs_project_id_cost_type",
                schema: "commercial",
                table: "project_costs",
                columns: new[] { "project_id", "cost_type" });

            migrationBuilder.CreateIndex(
                name: "ix_project_costs_source_id",
                schema: "commercial",
                table: "project_costs",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_costs_source_source_id_seq",
                schema: "commercial",
                table: "project_costs",
                columns: new[] { "source", "source_id", "seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "labor_cost_history",
                schema: "production");

            migrationBuilder.DropTable(
                name: "project_costs",
                schema: "commercial");

            migrationBuilder.DropIndex(
                name: "ix_labor_entries_cost_missing",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "cost",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "cost_backfilled",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "cost_missing",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "cost_position_id",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "cost_version",
                schema: "production",
                table: "labor_entries");

            migrationBuilder.DropColumn(
                name: "hourly_cost_snapshot",
                schema: "production",
                table: "labor_entries");
        }
    }
}
