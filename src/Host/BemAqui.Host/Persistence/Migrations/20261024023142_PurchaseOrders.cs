using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "installment",
                schema: "finance",
                table: "payables",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "text",
                schema: "purchasing",
                table: "parameters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quotes_count = table.Column<int>(type: "integer", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ipi_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    ipi_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    reference_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    changed_after_sent = table.Column<bool>(type: "boolean", nullable: false),
                    rejection_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_approvals",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    approval_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_approvals_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_levels",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    approval_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    reference_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    split_warning = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_levels", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_levels_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    entered_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    request_line_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payables_origin_installment",
                schema: "finance",
                table: "payables",
                columns: new[] { "origin", "origin_id", "installment" },
                unique: true,
                filter: "origin_id IS NOT NULL AND status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_approvals_order_attempt",
                schema: "purchasing",
                table: "purchase_order_approvals",
                columns: new[] { "order_id", "attempt" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_order_levels_attempt_level",
                schema: "purchasing",
                table: "purchase_order_levels",
                columns: new[] { "order_id", "attempt", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_item_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_party_submitted",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "party_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_project_id",
                schema: "purchasing",
                table: "purchase_orders",
                column: "project_id",
                filter: "project_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_rfq_id",
                schema: "purchasing",
                table: "purchase_orders",
                column: "rfq_id",
                filter: "rfq_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_status",
                schema: "purchasing",
                table: "purchase_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_orders_number",
                schema: "purchasing",
                table: "purchase_orders",
                column: "number",
                unique: true);

            // CMP-03a (ADR 0024 §2): os títulos de hoje ficam com installment = 1 pelo padrão da coluna; o índice novo nasce antes de o
            // antigo (origin, origin_id) sair, na migration PayablesOriginIndexStep2, e o código anterior continua valendo entre os dois passos.
            migrationBuilder.Sql("""
                ALTER TABLE finance.payables
                    ADD CONSTRAINT ck_payables_installment CHECK (installment >= 1);
                ALTER TABLE purchasing.purchase_orders
                    ADD CONSTRAINT ck_purchase_orders_status CHECK (status IN ('Draft', 'PendingApproval', 'Approved', 'Sent', 'PartiallyReceived', 'Received', 'Cancelled')),
                    ADD CONSTRAINT ck_purchase_orders_amounts CHECK (subtotal >= 0 AND freight >= 0 AND ipi_amount >= 0 AND total >= 0),
                    ADD CONSTRAINT ck_purchase_orders_ipi_pct CHECK (ipi_pct >= 0 AND ipi_pct <= 100),
                    ADD CONSTRAINT ck_purchase_orders_attempt CHECK (attempt >= 0),
                    ADD CONSTRAINT ck_purchase_orders_quotes CHECK (quotes_count IS NULL OR quotes_count >= 1);
                ALTER TABLE purchasing.purchase_order_lines
                    ADD CONSTRAINT ck_purchase_order_lines_quantity CHECK (quantity > 0 AND quantity_received >= 0 AND quantity_received <= quantity),
                    ADD CONSTRAINT ck_purchase_order_lines_price CHECK (unit_price > 0 AND line_total >= 0),
                    ADD CONSTRAINT ck_purchase_order_lines_cost_type CHECK (cost_type IN ('Material', 'Labor', 'Installation', 'Freight', 'Tax', 'Other'));
                ALTER TABLE purchasing.purchase_order_levels
                    ADD CONSTRAINT ck_purchase_order_levels_status CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Closed')),
                    ADD CONSTRAINT ck_purchase_order_levels_level CHECK (level BETWEEN 1 AND 3);
                ALTER TABLE purchasing.purchase_order_approvals
                    ADD CONSTRAINT ck_purchase_order_approvals_decision CHECK (decision IN ('Submitted', 'Approved', 'Rejected', 'Changed', 'Cancelled'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_approvals",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_order_levels",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchasing");

            migrationBuilder.DropIndex(
                name: "ux_payables_origin_installment",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "installment",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "text",
                schema: "purchasing",
                table: "parameters");
        }
    }
}
