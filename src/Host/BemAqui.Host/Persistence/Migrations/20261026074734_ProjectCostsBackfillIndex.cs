using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectCostsBackfillIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_project_costs_project_id_backfill",
                schema: "commercial",
                table: "project_costs",
                column: "project_id",
                filter: "is_backfill");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_costs_project_id_backfill",
                schema: "commercial",
                table: "project_costs");
        }
    }
}
