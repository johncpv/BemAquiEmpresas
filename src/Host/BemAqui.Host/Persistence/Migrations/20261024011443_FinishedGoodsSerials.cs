using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinishedGoodsSerials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completed_without_stock",
                schema: "production",
                table: "production_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "entry_movement_id",
                schema: "production",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "serial_start_value",
                schema: "inventory",
                table: "parameters",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "serial_sequences",
                schema: "inventory",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_sequences", x => new { x.item_id, x.year });
                    table.ForeignKey(
                        name: "fk_serial_sequences_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serials",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entry_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serials", x => x.id);
                    table.ForeignKey(
                        name: "fk_serials_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serials_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serials_stock_movements_entry_movement_id",
                        column: x => x.entry_movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_components",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_components", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_components_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_components_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_components_serials_serial_id",
                        column: x => x.serial_id,
                        principalSchema: "inventory",
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_serial_components_item_id",
                schema: "inventory",
                table: "serial_components",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_components_lot_id",
                schema: "inventory",
                table: "serial_components",
                column: "lot_id",
                filter: "lot_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_serial_components_order",
                schema: "inventory",
                table: "serial_components",
                columns: new[] { "production_order_id", "reversed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_components_serial_id",
                schema: "inventory",
                table: "serial_components",
                column: "serial_id",
                filter: "serial_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_serials_entry_movement_id",
                schema: "inventory",
                table: "serials",
                column: "entry_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_serials_item_status",
                schema: "inventory",
                table: "serials",
                columns: new[] { "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_serials_location_id",
                schema: "inventory",
                table: "serials",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_serials_production_order_id",
                schema: "inventory",
                table: "serials",
                column: "production_order_id",
                filter: "production_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_serials_project_id",
                schema: "inventory",
                table: "serials",
                column: "project_id",
                filter: "project_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_serials_item_number",
                schema: "inventory",
                table: "serials",
                columns: new[] { "item_id", "serial_number" },
                unique: true);

            // PRD-06a2: situação da série, quantidade do lote consumido e sequência positivas (CHECKs por SQL, como nas outras do estoque).
            migrationBuilder.Sql("ALTER TABLE inventory.serials ADD CONSTRAINT ck_serials_status CHECK (status IN ('InStock', 'Shipped', 'Cancelled'));");
            migrationBuilder.Sql("ALTER TABLE inventory.serial_components ADD CONSTRAINT ck_serial_components_quantity CHECK (quantity > 0);");
            migrationBuilder.Sql("ALTER TABLE inventory.serial_sequences ADD CONSTRAINT ck_serial_sequences_last_value CHECK (last_value >= 1);");
            migrationBuilder.Sql("ALTER TABLE inventory.parameters ADD CONSTRAINT ck_inventory_parameters_serial_start CHECK (serial_start_value >= 1);");

            // CA 9: as OS concluídas pela PRD-06a1 ficam "Concluída antes do controle de estoque", sem entrada nem séries (sem recomposição).
            migrationBuilder.Sql(BemAqui.Production.Infrastructure.ProductionDataFixes.CompletedWithoutStock);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "serial_components",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "serial_sequences",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "serials",
                schema: "inventory");

            migrationBuilder.DropColumn(
                name: "completed_without_stock",
                schema: "production",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "entry_movement_id",
                schema: "production",
                table: "production_orders");

            migrationBuilder.Sql("ALTER TABLE inventory.parameters DROP CONSTRAINT IF EXISTS ck_inventory_parameters_serial_start;");

            migrationBuilder.DropColumn(
                name: "serial_start_value",
                schema: "inventory",
                table: "parameters");
        }
    }
}
