using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesCoreVnd01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.AddColumn<Guid>(
                name: "cash_session_id",
                schema: "finance",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_fee",
                schema: "finance",
                table: "receivables",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_amount",
                schema: "finance",
                table: "receivables",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                schema: "finance",
                table: "receivables",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sale_channel",
                schema: "finance",
                table: "receivables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sale_group_key",
                schema: "finance",
                table: "receivables",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "settlement_date",
                schema: "finance",
                table: "receivables",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stock_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fiscal_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    register_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seller_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    service_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    from_device = table.Column<bool>(type: "boolean", nullable: false),
                    price_list_version = table.Column<long>(type: "bigint", nullable: true),
                    completed_at_device = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.CheckConstraint("ck_sales_no_register_channels", "channel NOT IN ('Online', 'ServiceOrder') OR (register_id IS NULL AND cash_session_id IS NULL)");
                    table.CheckConstraint("ck_sales_pos_register", "channel <> 'Pos' OR (register_id IS NOT NULL AND cash_session_id IS NOT NULL)");
                    table.CheckConstraint("ck_sales_totals", "subtotal >= 0 AND discount_total >= 0 AND service_fee >= 0 AND total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_lines", x => x.id);
                    table.CheckConstraint("ck_sale_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sale_lines_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_parts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_parts_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_postings",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_postings", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_postings_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    change_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    integration = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    installments = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    acquirer_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    card_brand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    nsu = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    authorization_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    txid = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    end_to_end_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    statement_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_payments", x => x.id);
                    table.CheckConstraint("ck_sale_payments_amount", "amount > 0 AND change_amount >= 0 AND change_amount < amount");
                    table.CheckConstraint("ck_sale_payments_installments", "installments BETWEEN 1 AND 24");
                    table.ForeignKey(
                        name: "fk_sale_payments_sale_parts_sale_part_id",
                        column: x => x.sale_part_id,
                        principalSchema: "sales",
                        principalTable: "sale_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receivables_sale_day_channel",
                schema: "finance",
                table: "receivables",
                columns: new[] { "issue_date", "sale_channel" },
                filter: "sale_channel IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_receivables_sale_group_key",
                schema: "finance",
                table: "receivables",
                column: "sale_group_key",
                unique: true,
                filter: "sale_group_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_parent_product_id",
                schema: "sales",
                table: "sale_lines",
                column: "parent_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_product_id",
                schema: "sales",
                table: "sale_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_sale_id_line_number",
                schema: "sales",
                table: "sale_lines",
                columns: new[] { "sale_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_parts_sale_id",
                schema: "sales",
                table: "sale_parts",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_payments_payment_method_id",
                schema: "sales",
                table: "sale_payments",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_payments_sale_id",
                schema: "sales",
                table: "sale_payments",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_payments_sale_part_id",
                schema: "sales",
                table: "sale_payments",
                column: "sale_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_cash_session_id",
                schema: "sales",
                table: "sales",
                column: "cash_session_id",
                filter: "cash_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_completed_day_channel",
                schema: "sales",
                table: "sales",
                columns: new[] { "business_date", "channel" },
                filter: "status = 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_source_type_source_id",
                schema: "sales",
                table: "sales",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_channel_number",
                schema: "sales",
                table: "sales",
                columns: new[] { "channel", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_postings_stock_movement_id",
                schema: "sales",
                table: "stock_postings",
                column: "stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_postings_sale_item",
                schema: "sales",
                table: "stock_postings",
                columns: new[] { "sale_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sale_payments",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "stock_postings",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sale_parts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_receivables_sale_day_channel",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropIndex(
                name: "ux_receivables_sale_group_key",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "cash_session_id",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "expected_fee",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "gross_amount",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "net_amount",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "sale_channel",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "sale_group_key",
                schema: "finance",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "settlement_date",
                schema: "finance",
                table: "receivables");
        }
    }
}
