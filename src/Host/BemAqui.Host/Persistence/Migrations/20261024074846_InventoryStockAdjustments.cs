using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryStockAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_adjustments",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason_detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    surplus_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    shortage_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reversal_self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustment_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_qty_at_request = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    counted_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    delta_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost_estimate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entered_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_qty_at_approval = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    applied_delta_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustment_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_stock_adjustments_adjustment_id",
                        column: x => x.adjustment_id,
                        principalSchema: "inventory",
                        principalTable: "stock_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_stock_movements_movement_id",
                        column: x => x.movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_item_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_lot_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_adjustment_lines_adjustment_item_lot",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "adjustment_id", "item_id", "lot_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_stock_adjustment_lines_adjustment_sequence",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "adjustment_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_location_status",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_origin_id",
                schema: "inventory",
                table: "stock_adjustments",
                column: "origin_id",
                filter: "origin_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_status_requested_at",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_adjustments_number",
                schema: "inventory",
                table: "stock_adjustments",
                column: "number",
                unique: true);

            // EST-01b: modo, origem, motivo e situação conhecidos; contado nunca negativo e só no modo Counted; valores não negativos.
            migrationBuilder.Sql(
                """
                ALTER TABLE inventory.stock_adjustments
                    ADD CONSTRAINT ck_stock_adjustments_mode CHECK (mode IN ('Counted', 'Delta')),
                    ADD CONSTRAINT ck_stock_adjustments_origin CHECK (origin IN ('Manual', 'InventoryCount')),
                    ADD CONSTRAINT ck_stock_adjustments_reason CHECK (reason IN ('Loss', 'EntryError', 'Surplus', 'Count', 'Other')),
                    ADD CONSTRAINT ck_stock_adjustments_status CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
                    ADD CONSTRAINT ck_stock_adjustments_values CHECK (surplus_value >= 0 AND shortage_value >= 0);
                ALTER TABLE inventory.stock_adjustment_lines
                    ADD CONSTRAINT ck_stock_adjustment_lines_counted CHECK (counted_qty IS NULL OR counted_qty >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_adjustment_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_adjustments",
                schema: "inventory");
        }
    }
}
