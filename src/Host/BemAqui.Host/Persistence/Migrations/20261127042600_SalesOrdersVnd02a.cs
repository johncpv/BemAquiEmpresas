using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesOrdersVnd02a : Migration
    {
        /// <inheritdoc />
        // VND-02a: pedido de venda (sales.orders, order_lines, order_transitions, order_parameters), receivable_id no pagamento da venda, devolução no
        // título e a reserva genérica (CA 11). Só aditiva: o índice da OS é recriado como parcial com as mesmas colunas (nada muda para a OS).
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_reservations_order_item_location",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.AddColumn<Guid>(
                name: "receivable_id",
                schema: "sales",
                table: "sale_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "production_order_id",
                schema: "inventory",
                table: "reservations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "inventory",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                schema: "inventory",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "inventory",
                table: "reservations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refunded_at",
                schema: "finance",
                table: "receivables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "refunded_by",
                schema: "finance",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_parameters",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_limit_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    app_discount_limit_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    hold_hours = table.Column<int>(type: "integer", nullable: false),
                    app_hold_hours = table.Column<int>(type: "integer", nullable: false),
                    charge_days = table.Column<int>(type: "integer", nullable: false),
                    app_charge_days = table.Column<int>(type: "integer", nullable: false),
                    app_max_line_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    app_max_order_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    app_unpaid_alert_count = table.Column<int>(type: "integer", nullable: false),
                    bank_slip_payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_app_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_app_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    external_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    external_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    order_discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    hold_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_needs_approval = table.Column<bool>(type: "boolean", nullable: false),
                    paid_after_cancel = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    charged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_totals", "subtotal >= 0 AND discount_total >= 0 AND order_discount >= 0 AND total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_list_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_quantity", "quantity > 0 AND discount >= 0 AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_transitions",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_transitions_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservations_expires_at",
                schema: "inventory",
                table: "reservations",
                column: "expires_at",
                filter: "expires_at IS NOT NULL AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_order_item_location",
                schema: "inventory",
                table: "reservations",
                columns: new[] { "production_order_id", "item_id", "location_id" },
                unique: true,
                filter: "production_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_source_item_location",
                schema: "inventory",
                table: "reservations",
                columns: new[] { "source_type", "source_id", "item_id", "location_id" },
                unique: true,
                filter: "source_type IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservations_single_origin",
                schema: "inventory",
                table: "reservations",
                sql: "(production_order_id IS NOT NULL AND source_type IS NULL AND source_id IS NULL) OR (production_order_id IS NULL AND source_type IS NOT NULL AND source_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id_line_number",
                schema: "sales",
                table: "order_lines",
                columns: new[] { "order_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_product_id",
                schema: "sales",
                table: "order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_transitions_order_id_at",
                schema: "sales",
                table: "order_transitions",
                columns: new[] { "order_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_source_app_id",
                schema: "sales",
                table: "orders",
                columns: new[] { "customer_id", "source_app_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_hold_until",
                schema: "sales",
                table: "orders",
                column: "hold_until",
                filter: "hold_until IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_receivable_id",
                schema: "sales",
                table: "orders",
                column: "receivable_id",
                filter: "receivable_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_updated",
                schema: "sales",
                table: "orders",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_number",
                schema: "sales",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_origin_app_external",
                schema: "sales",
                table: "orders",
                columns: new[] { "origin", "source_app_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_parameters",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_transitions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_reservations_expires_at",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "ux_reservations_order_item_location",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "ux_reservations_source_item_location",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_single_origin",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "receivable_id",
                schema: "sales",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "source_id",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "inventory",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "refunded_by",
                schema: "finance",
                table: "receivables");

            migrationBuilder.AlterColumn<Guid>(
                name: "production_order_id",
                schema: "inventory",
                table: "reservations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_reservations_order_item_location",
                schema: "inventory",
                table: "reservations",
                columns: new[] { "production_order_id", "item_id", "location_id" },
                unique: true);
        }
    }
}
