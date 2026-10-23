using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryStockEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lots",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    supplier_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lots", x => x.id);
                    table.ForeignKey(
                        name: "fk_lots_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manual_postings",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approval_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estimated_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cutoff_date = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_postings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parameters",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manual_approval_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    manual_cost_deviation_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    opening_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opening_completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_positions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_positions_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_positions_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_balances_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    average_cost_after = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    posted_seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    leg = table.Column<int>(type: "integer", nullable: false),
                    reverses_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_stock_movements_reverses_movement_id",
                        column: x => x.reverses_movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manual_posting_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entered_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reversal_self_approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_posting_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_manual_posting_lines_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manual_posting_lines_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manual_posting_lines_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manual_posting_lines_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manual_posting_lines_manual_postings_posting_id",
                        column: x => x.posting_id,
                        principalSchema: "inventory",
                        principalTable: "manual_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_lots_item_number",
                schema: "inventory",
                table: "lots",
                columns: new[] { "item_id", "lot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_in_movement_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "in_movement_id",
                filter: "in_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_item_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_location_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_lot_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_movement_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "movement_id",
                filter: "movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_manual_posting_lines_to_location_id",
                schema: "inventory",
                table: "manual_posting_lines",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ux_manual_posting_lines_posting_sequence",
                schema: "inventory",
                table: "manual_posting_lines",
                columns: new[] { "posting_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manual_postings_status_requested_at",
                schema: "inventory",
                table: "manual_postings",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_manual_postings_number",
                schema: "inventory",
                table: "manual_postings",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_location_id",
                schema: "inventory",
                table: "stock_balances",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_lot_id",
                schema: "inventory",
                table: "stock_balances",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_balances_item_location_lot",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "item_id", "location_id", "lot_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_seq",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "item_id", "posted_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_location_item",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "location_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_lot_id",
                schema: "inventory",
                table: "stock_movements",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_number",
                schema: "inventory",
                table: "stock_movements",
                column: "number");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_origin_posted_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "origin", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_posted_seq",
                schema: "inventory",
                table: "stock_movements",
                column: "posted_seq");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_production_order_id",
                schema: "inventory",
                table: "stock_movements",
                column: "production_order_id",
                filter: "production_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_project_id",
                schema: "inventory",
                table: "stock_movements",
                column: "project_id",
                filter: "project_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_reverses",
                schema: "inventory",
                table: "stock_movements",
                column: "reverses_movement_id",
                unique: true,
                filter: "reverses_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_source_leg_type",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "source_type", "source_id", "leg", "type" },
                unique: true,
                filter: "reverses_movement_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_positions_location_id",
                schema: "inventory",
                table: "stock_positions",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_positions_item_location",
                schema: "inventory",
                table: "stock_positions",
                columns: new[] { "item_id", "location_id" },
                unique: true);

            // ADR 0023 §2: o banco recusa saldo negativo e reserva acima do físico, mesmo com bug na aplicação (EST-01a CA 14).
            migrationBuilder.Sql("""
                ALTER TABLE inventory.stock_positions
                    ADD CONSTRAINT ck_stock_positions_on_hand CHECK (quantity_on_hand >= 0),
                    ADD CONSTRAINT ck_stock_positions_reserved CHECK (quantity_reserved BETWEEN 0 AND quantity_on_hand);
                ALTER TABLE inventory.stock_balances
                    ADD CONSTRAINT ck_stock_balances_on_hand CHECK (quantity_on_hand >= 0);
                """);

            // ADR 0023 §4: movimento de estoque é imutável; qualquer UPDATE ou DELETE é recusado (EST-01a CA 22). Estorno é movimento inverso.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.stock_movements_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'Movimento de estoque não muda nem é apagado; estorne-o.' USING ERRCODE = 'restrict_violation';
                END;
                $fn$;
                CREATE TRIGGER trg_stock_movements_immutable
                    BEFORE UPDATE OR DELETE ON inventory.stock_movements
                    FOR EACH ROW EXECUTE FUNCTION inventory.stock_movements_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_stock_movements_immutable ON inventory.stock_movements;
                DROP FUNCTION IF EXISTS inventory.stock_movements_immutable();
                """);

            migrationBuilder.DropTable(
                name: "manual_posting_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "parameters",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_positions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "manual_postings",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "lots",
                schema: "inventory");
        }
    }
}
