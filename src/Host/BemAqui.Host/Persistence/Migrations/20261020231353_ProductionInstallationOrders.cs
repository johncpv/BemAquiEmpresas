using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionInstallationOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_template",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_orders",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: true),
                    accepted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    accepted_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_order_checklist",
                schema: "production",
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
                    table.PrimaryKey("pk_production_order_checklist", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_checklist_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_order_project_items",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_order_project_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_project_items_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_order_steps",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_order_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_steps_production_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "production",
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_production_order_checklist_order_id",
                schema: "production",
                table: "production_order_checklist",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_project_items_order_id_project_item_id",
                schema: "production",
                table: "production_order_project_items",
                columns: new[] { "order_id", "project_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_order_project_items_project_item_id",
                schema: "production",
                table: "production_order_project_items",
                column: "project_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_steps_order_id",
                schema: "production",
                table: "production_order_steps",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_installer_id",
                schema: "production",
                table: "production_orders",
                column: "installer_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_number",
                schema: "production",
                table: "production_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_project_id_type",
                schema: "production",
                table: "production_orders",
                columns: new[] { "project_id", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_template",
                schema: "production");

            migrationBuilder.DropTable(
                name: "production_order_checklist",
                schema: "production");

            migrationBuilder.DropTable(
                name: "production_order_project_items",
                schema: "production");

            migrationBuilder.DropTable(
                name: "production_order_steps",
                schema: "production");

            migrationBuilder.DropTable(
                name: "production_orders",
                schema: "production");
        }
    }
}
