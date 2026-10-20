using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancePaymentOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payable_id",
                schema: "finance",
                table: "financial_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payable_settlements",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    backdate_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reversal_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_settlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_order_approvals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_order_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_order_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payee_snapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_order_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    paid_by = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_transactions_payable_id",
                schema: "finance",
                table: "financial_transactions",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payable_settlements_payable_id",
                schema: "finance",
                table: "payable_settlements",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payable_settlements_payment_order_id",
                schema: "finance",
                table: "payable_settlements",
                column: "payment_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_order_approvals_payment_order_id",
                schema: "finance",
                table: "payment_order_approvals",
                column: "payment_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_order_items_payable_id",
                schema: "finance",
                table: "payment_order_items",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_order_items_payment_order_id",
                schema: "finance",
                table: "payment_order_items",
                column: "payment_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_number",
                schema: "finance",
                table: "payment_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_status_submitted_at",
                schema: "finance",
                table: "payment_orders",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payable_settlements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_order_approvals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_order_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_orders",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_financial_transactions_payable_id",
                schema: "finance",
                table: "financial_transactions");

            migrationBuilder.DropColumn(
                name: "payable_id",
                schema: "finance",
                table: "financial_transactions");
        }
    }
}
