using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionKanbanStepCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completed_by_supervisor",
                schema: "production",
                table: "production_order_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "completion_reason",
                schema: "production",
                table: "production_order_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_by_supervisor",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "completion_reason",
                schema: "production",
                table: "production_order_steps");
        }
    }
}
