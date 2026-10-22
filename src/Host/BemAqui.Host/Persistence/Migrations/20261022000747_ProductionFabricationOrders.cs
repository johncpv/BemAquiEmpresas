using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionFabricationOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "routing_template_id",
                schema: "commercial",
                table: "quote_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "routing_template_id",
                schema: "commercial",
                table: "project_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "planned_start",
                schema: "production",
                table: "production_orders",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "planned_end",
                schema: "production",
                table: "production_orders",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<Guid>(
                name: "installer_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<decimal>(
                name: "contract_value",
                schema: "production",
                table: "production_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_end",
                schema: "production",
                table: "production_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_start",
                schema: "production",
                table: "production_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                schema: "production",
                table: "production_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "product_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_item_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                schema: "production",
                table: "production_orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "routing_template_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_end",
                schema: "production",
                table: "production_order_steps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_start",
                schema: "production",
                table: "production_order_steps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "production",
                table: "production_order_steps",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_quality_step",
                schema: "production",
                table: "production_order_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_end",
                schema: "production",
                table: "production_order_steps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_start",
                schema: "production",
                table: "production_order_steps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_approval",
                schema: "production",
                table: "production_order_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "standard_hours",
                schema: "production",
                table: "production_order_steps",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "production",
                table: "production_order_steps",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "step_id",
                schema: "production",
                table: "production_order_checklist",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_quote_items_routing_template_id",
                schema: "commercial",
                table: "quote_items",
                column: "routing_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_items_routing_template_id",
                schema: "commercial",
                table: "project_items",
                column: "routing_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_product_id",
                schema: "production",
                table: "production_orders",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_routing_template_id",
                schema: "production",
                table: "production_orders",
                column: "routing_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_type_status",
                schema: "production",
                table: "production_orders",
                columns: new[] { "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_production_orders_fabrication_item",
                schema: "production",
                table: "production_orders",
                column: "project_item_id",
                unique: true,
                filter: "type = 'Fabrication' AND status <> 'Cancelled'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_orders_fabrication",
                schema: "production",
                table: "production_orders",
                sql: "type <> 'Fabrication' OR (installer_id IS NULL AND contract_value IS NULL AND project_item_id IS NOT NULL AND quantity IS NOT NULL AND quantity > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_production_orders_installation",
                schema: "production",
                table: "production_orders",
                sql: "type <> 'Installation' OR (installer_id IS NOT NULL AND contract_value IS NOT NULL AND planned_start IS NOT NULL AND planned_end IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_steps_work_center_id",
                schema: "production",
                table: "production_order_steps",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_checklist_step_id",
                schema: "production",
                table: "production_order_checklist",
                column: "step_id");

            migrationBuilder.AddForeignKey(
                name: "fk_production_order_checklist_production_order_steps_step_id",
                schema: "production",
                table: "production_order_checklist",
                column: "step_id",
                principalSchema: "production",
                principalTable: "production_order_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_production_order_checklist_production_order_steps_step_id",
                schema: "production",
                table: "production_order_checklist");

            migrationBuilder.DropIndex(
                name: "ix_quote_items_routing_template_id",
                schema: "commercial",
                table: "quote_items");

            migrationBuilder.DropIndex(
                name: "ix_project_items_routing_template_id",
                schema: "commercial",
                table: "project_items");

            migrationBuilder.DropIndex(
                name: "ix_production_orders_product_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropIndex(
                name: "ix_production_orders_routing_template_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropIndex(
                name: "ix_production_orders_type_status",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropIndex(
                name: "ux_production_orders_fabrication_item",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_production_orders_fabrication",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_production_orders_installation",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropIndex(
                name: "ix_production_order_steps_work_center_id",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropIndex(
                name: "ix_production_order_checklist_step_id",
                schema: "production",
                table: "production_order_checklist");

            migrationBuilder.DropColumn(
                name: "routing_template_id",
                schema: "commercial",
                table: "quote_items");

            migrationBuilder.DropColumn(
                name: "routing_template_id",
                schema: "commercial",
                table: "project_items");

            migrationBuilder.DropColumn(
                name: "actual_end",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "actual_start",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "product_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "project_item_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "routing_template_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "actual_end",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "actual_start",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "is_quality_step",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "planned_end",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "planned_start",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "requires_approval",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "standard_hours",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "production",
                table: "production_order_steps");

            migrationBuilder.DropColumn(
                name: "step_id",
                schema: "production",
                table: "production_order_checklist");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "planned_start",
                schema: "production",
                table: "production_orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "planned_end",
                schema: "production",
                table: "production_orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "installer_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "contract_value",
                schema: "production",
                table: "production_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
