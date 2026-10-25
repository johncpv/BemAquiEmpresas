using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_counts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    snapshot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uncounted_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    adjustment_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopen_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_counts_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_count_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    counted_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    entered_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    system_qty_at_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    movement_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    recount_of_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded = table.Column<bool>(type: "boolean", nullable: false),
                    added_during_count = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_count_lines_inventory_count_lines_recount_of_line",
                        column: x => x.recount_of_line_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_count_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_count_lines_inventory_counts_count_id",
                        column: x => x.count_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_count_lines_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_count_lines_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_count_lines_item_id",
                schema: "inventory",
                table: "inventory_count_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_count_lines_lot_id",
                schema: "inventory",
                table: "inventory_count_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_count_lines_recount_of_line_id",
                schema: "inventory",
                table: "inventory_count_lines",
                column: "recount_of_line_id",
                filter: "recount_of_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_count_lines_count_item_lot",
                schema: "inventory",
                table: "inventory_count_lines",
                columns: new[] { "count_id", "item_id", "lot_id" },
                unique: true,
                filter: "superseded = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_inventory_count_lines_count_sequence",
                schema: "inventory",
                table: "inventory_count_lines",
                columns: new[] { "count_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_counts_adjustment_id",
                schema: "inventory",
                table: "inventory_counts",
                column: "adjustment_id",
                filter: "adjustment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_counts_status_snapshot_at",
                schema: "inventory",
                table: "inventory_counts",
                columns: new[] { "status", "snapshot_at" });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_counts_location_counting",
                schema: "inventory",
                table: "inventory_counts",
                column: "location_id",
                unique: true,
                filter: "status = 'Counting'");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_counts_number",
                schema: "inventory",
                table: "inventory_counts",
                column: "number",
                unique: true);

            // EST-05: situação e escolha dos não contados só com os valores do domínio; contagem nunca negativa.
            migrationBuilder.Sql("""
                ALTER TABLE inventory.inventory_counts
                    ADD CONSTRAINT ck_inventory_counts_status CHECK (status IN ('Counting', 'Closed', 'Cancelled')),
                    ADD CONSTRAINT ck_inventory_counts_uncounted_policy CHECK (uncounted_policy IS NULL OR uncounted_policy IN ('Zero', 'KeepSystem'));
                ALTER TABLE inventory.inventory_count_lines
                    ADD CONSTRAINT ck_inventory_count_lines_counted CHECK (counted_qty IS NULL OR counted_qty >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_count_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_counts",
                schema: "inventory");
        }
    }
}
