using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionOrderCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "fabricated_by_orders",
                schema: "commercial",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "completion_reversal_reason",
                schema: "production",
                table: "production_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completion_reversed_at",
                schema: "production",
                table: "production_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "completion_reversed_by",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            // PRD-06a1 CA 1: as OS de fabricação com todas as etapas concluídas passam ao estado gravado "Pronta para concluir".
            migrationBuilder.Sql(BemAqui.Production.Infrastructure.ProductionDataFixes.ReadyToComplete);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A versão anterior não conhece ReadyToComplete: a OS volta a InProgress (a situação calculada "Pronta para concluir").
            migrationBuilder.Sql("UPDATE production.production_orders SET status = 'InProgress' WHERE status = 'ReadyToComplete';");

            migrationBuilder.DropColumn(
                name: "fabricated_by_orders",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "completed_by",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "completion_reversal_reason",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "completion_reversed_at",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "completion_reversed_by",
                schema: "production",
                table: "production_orders");
        }
    }
}
