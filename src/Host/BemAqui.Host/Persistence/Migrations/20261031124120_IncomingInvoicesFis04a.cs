using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncomingInvoicesFis04a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incoming_invoices",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    series = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issuer_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuer_trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issuer_state_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    issuer_address = table.Column<string>(type: "jsonb", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    xml_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    totals = table.Column<string>(type: "jsonb", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    duplicates = table.Column<string>(type: "jsonb", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    signer_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sefaz_status_checked = table.Column<bool>(type: "boolean", nullable: false),
                    order_references = table.Column<string[]>(type: "text[]", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    without_order = table.Column<bool>(type: "boolean", nullable: false),
                    without_order_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    without_order_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    divergences = table.Column<string>(type: "jsonb", nullable: false),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rejected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoming_invoices", x => x.id);
                    table.CheckConstraint("ck_incoming_invoices_access_key", "length(access_key) = 44");
                });

            migrationBuilder.CreateTable(
                name: "incoming_invoice_items",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(24,10)", precision: 24, scale: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxes = table.Column<string>(type: "jsonb", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    order_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    order_item = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    stock_unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    no_stock_item = table.Column<bool>(type: "boolean", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mapping_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoming_invoice_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_incoming_invoice_items_incoming_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "fiscal",
                        principalTable: "incoming_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_incoming_invoice_items_invoice_sequence",
                schema: "fiscal",
                table: "incoming_invoice_items",
                columns: new[] { "invoice_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incoming_invoices_party_id",
                schema: "fiscal",
                table: "incoming_invoices",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_incoming_invoices_status_imported_at",
                schema: "fiscal",
                table: "incoming_invoices",
                columns: new[] { "status", "imported_at" });

            migrationBuilder.CreateIndex(
                name: "ux_incoming_invoices_access_key",
                schema: "fiscal",
                table: "incoming_invoices",
                column: "access_key",
                unique: true,
                filter: "status <> 'Rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incoming_invoice_items",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "incoming_invoices",
                schema: "fiscal");
        }
    }
}
