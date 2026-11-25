using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pdv06SaleReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "store_credit_validity_days",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "sales",
                table: "sales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.CreateTable(
                name: "sale_returns",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    refund_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    late = table.Column<bool>(type: "boolean", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    card_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    card_nsu = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    is_exchange = table.Column<bool>(type: "boolean", nullable: false),
                    exchange_sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    store_credit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supervisor_approval_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_returns", x => x.id);
                    table.CheckConstraint("ck_sale_returns_card", "refund_kind <> 'CardReversal' OR (card_payment_id IS NOT NULL AND card_nsu IS NOT NULL)");
                    table.CheckConstraint("ck_sale_returns_total", "total > 0");
                    table.ForeignKey(
                        name: "fk_sale_returns_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_credits",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    origin_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_credits", x => x.id);
                    table.CheckConstraint("ck_store_credits_balance", "amount > 0 AND balance >= 0 AND balance <= amount");
                });

            migrationBuilder.CreateTable(
                name: "sale_return_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    destination = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loss_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_return_lines", x => x.id);
                    table.CheckConstraint("ck_sale_return_lines_quantity", "quantity > 0 AND amount >= 0");
                    table.ForeignKey(
                        name: "fk_sale_return_lines_sale_lines_sale_line_id",
                        column: x => x.sale_line_id,
                        principalSchema: "sales",
                        principalTable: "sale_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_lines_sale_returns_return_id",
                        column: x => x.return_id,
                        principalSchema: "sales",
                        principalTable: "sale_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_credit_movements",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_credit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_credit_movements", x => x.id);
                    table.CheckConstraint("ck_store_credit_movements_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_store_credit_movements_store_credits_store_credit_id",
                        column: x => x.store_credit_id,
                        principalSchema: "sales",
                        principalTable: "store_credits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_store_credit_validity",
                schema: "pos",
                table: "stores",
                sql: "store_credit_validity_days BETWEEN 1 AND 3650");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_lines_return_id",
                schema: "sales",
                table: "sale_return_lines",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_lines_sale_line_id",
                schema: "sales",
                table: "sale_return_lines",
                column: "sale_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_business_date_store_id",
                schema: "sales",
                table: "sale_returns",
                columns: new[] { "business_date", "store_id" },
                filter: "status = 'Completed'");

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_card_payment_id",
                schema: "sales",
                table: "sale_returns",
                column: "card_payment_id",
                filter: "card_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_number",
                schema: "sales",
                table: "sale_returns",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_sale_id",
                schema: "sales",
                table: "sale_returns",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_credit_movements_sale_id",
                schema: "sales",
                table: "store_credit_movements",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_credit_movements_store_credit_id",
                schema: "sales",
                table: "store_credit_movements",
                column: "store_credit_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_credit_movements_tender_id",
                schema: "sales",
                table: "store_credit_movements",
                column: "tender_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_credits_code_hash",
                schema: "sales",
                table: "store_credits",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_credits_origin_return_id",
                schema: "sales",
                table: "store_credits",
                column: "origin_return_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_return_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "store_credit_movements",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sale_returns",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "store_credits",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_store_credit_validity",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "store_credit_validity_days",
                schema: "pos",
                table: "stores");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "sales",
                table: "sales",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
