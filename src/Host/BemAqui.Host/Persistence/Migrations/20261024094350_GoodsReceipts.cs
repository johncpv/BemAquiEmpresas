using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoodsReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "approved_total",
                schema: "purchasing",
                table: "purchase_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                schema: "purchasing",
                table: "purchase_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "purchasing",
                table: "purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "closed_balance_amount",
                schema: "purchasing",
                table: "purchase_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                schema: "purchasing",
                table: "purchase_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "amount_received",
                schema: "purchasing",
                table: "purchase_order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity_over_received",
                schema: "purchasing",
                table: "purchase_order_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incoming_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ipi_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    order_status_before = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    sole_owner = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipts_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_price_corrections",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    new_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reapproval = table.Column<bool>(type: "boolean", nullable: false),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_price_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_price_corrections_purchase_order_lines_po_li",
                        column: x => x.po_line_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_price_corrections_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    supplier_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    supplier_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    lot_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    freight_share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ipi_share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reversal_movement_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_goods_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "purchasing",
                        principalTable: "goods_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_purchase_order_lines_po_line_id",
                        column: x => x.po_line_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_item_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_po_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                column: "po_line_id");

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipt_lines_receipt_po_line",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "receipt_id", "po_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipt_lines_receipt_sequence",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "receipt_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_status_received_on",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "status", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipts_number",
                schema: "purchasing",
                table: "goods_receipts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_price_corrections_order_id",
                schema: "purchasing",
                table: "purchase_order_price_corrections",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_price_corrections_po_line_id",
                schema: "purchasing",
                table: "purchase_order_price_corrections",
                column: "po_line_id");

            // CMP-04: CHECKs por SQL (ADR 0019) e o total aprovado dos pedidos já aprovados (base do teto, D8). Nada destrutivo.
            migrationBuilder.Sql("""
                ALTER TABLE purchasing.goods_receipts
                    ADD CONSTRAINT ck_goods_receipts_status CHECK (status IN ('Completed', 'Reversed')),
                    ADD CONSTRAINT ck_goods_receipts_amounts CHECK (subtotal >= 0 AND freight >= 0 AND ipi_amount >= 0 AND amount > 0 AND amount = subtotal + freight + ipi_amount),
                    ADD CONSTRAINT ck_goods_receipts_reversal CHECK ((status = 'Reversed') = (reversed_at IS NOT NULL));
                ALTER TABLE purchasing.goods_receipt_lines
                    ADD CONSTRAINT ck_goods_receipt_lines_quantity CHECK (quantity > 0 AND (supplier_quantity IS NULL OR supplier_quantity > 0)),
                    ADD CONSTRAINT ck_goods_receipt_lines_amounts CHECK (unit_price > 0 AND line_amount >= 0 AND freight_share >= 0 AND ipi_share >= 0 AND unit_cost > 0);
                ALTER TABLE purchasing.purchase_order_lines
                    ADD CONSTRAINT ck_purchase_order_lines_received CHECK (quantity_over_received >= 0 AND amount_received >= 0);
                ALTER TABLE purchasing.purchase_order_price_corrections
                    ADD CONSTRAINT ck_purchase_order_price_corrections_price CHECK (previous_price > 0 AND new_price > 0 AND new_price <> previous_price);
                UPDATE purchasing.purchase_orders SET approved_total = total
                    WHERE approved_at IS NOT NULL AND approved_total IS NULL AND status IN ('Approved', 'Sent', 'PartiallyReceived', 'Received');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_order_lines DROP CONSTRAINT IF EXISTS ck_purchase_order_lines_received;");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_order_price_corrections",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "goods_receipts",
                schema: "purchasing");

            migrationBuilder.DropColumn(
                name: "approved_total",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "close_reason",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "closed_balance_amount",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "closed_by",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "amount_received",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "quantity_over_received",
                schema: "purchasing",
                table: "purchase_order_lines");
        }
    }
}
