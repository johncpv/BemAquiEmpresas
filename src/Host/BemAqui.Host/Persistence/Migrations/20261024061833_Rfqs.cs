using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rfqs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rfqs",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfqs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rfq_suppliers",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ipi_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    payment_term_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responded = table.Column<bool>(type: "boolean", nullable: false),
                    declined = table.Column<bool>(type: "boolean", nullable: false),
                    responded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    order_submit_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_suppliers_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "purchasing",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    selected_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selection_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    selection_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_lines_rfq_suppliers_selected_supplier_id",
                        column: x => x.selected_supplier_id,
                        principalSchema: "purchasing",
                        principalTable: "rfq_suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_lines_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "purchasing",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_line_requests",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_line_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_line_requests_purchase_request_lines_request_line_id",
                        column: x => x.request_line_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_request_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_line_requests_rfq_lines_rfq_line_id",
                        column: x => x.rfq_line_id,
                        principalSchema: "purchasing",
                        principalTable: "rfq_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_line_requests_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "purchasing",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_quotes",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    supplier_unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    supplier_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    available_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_quotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_quotes_rfq_lines_rfq_line_id",
                        column: x => x.rfq_line_id,
                        principalSchema: "purchasing",
                        principalTable: "rfq_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_quotes_rfq_suppliers_rfq_supplier_id",
                        column: x => x.rfq_supplier_id,
                        principalSchema: "purchasing",
                        principalTable: "rfq_suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rfq_quotes_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "purchasing",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rfq_line_requests_request_line_id",
                schema: "purchasing",
                table: "rfq_line_requests",
                column: "request_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_line_requests_rfq_id",
                schema: "purchasing",
                table: "rfq_line_requests",
                column: "rfq_id");

            migrationBuilder.CreateIndex(
                name: "ux_rfq_line_requests_line_request",
                schema: "purchasing",
                table: "rfq_line_requests",
                columns: new[] { "rfq_line_id", "request_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_selected_supplier_id",
                schema: "purchasing",
                table: "rfq_lines",
                column: "selected_supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_rfq_lines_rfq_item",
                schema: "purchasing",
                table: "rfq_lines",
                columns: new[] { "rfq_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rfq_lines_rfq_sequence",
                schema: "purchasing",
                table: "rfq_lines",
                columns: new[] { "rfq_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_quotes_rfq_id",
                schema: "purchasing",
                table: "rfq_quotes",
                column: "rfq_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_quotes_rfq_supplier_id",
                schema: "purchasing",
                table: "rfq_quotes",
                column: "rfq_supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_rfq_quotes_line_supplier",
                schema: "purchasing",
                table: "rfq_quotes",
                columns: new[] { "rfq_line_id", "rfq_supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_suppliers_party_id",
                schema: "purchasing",
                table: "rfq_suppliers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ux_rfq_suppliers_rfq_party",
                schema: "purchasing",
                table: "rfq_suppliers",
                columns: new[] { "rfq_id", "party_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_status_created_at",
                schema: "purchasing",
                table: "rfqs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_rfqs_number",
                schema: "purchasing",
                table: "rfqs",
                column: "number",
                unique: true);

            // CMP-02: as regras que o banco também segura (situação, valores sem negativo, quantidade e preço maiores que zero, prazo de 0 a 365).
            migrationBuilder.Sql("""
                ALTER TABLE purchasing.rfqs
                    ADD CONSTRAINT ck_rfqs_status CHECK (status IN ('Open', 'Closed', 'Cancelled'));
                ALTER TABLE purchasing.rfq_suppliers
                    ADD CONSTRAINT ck_rfq_suppliers_values CHECK (freight >= 0 AND ipi_pct >= 0 AND ipi_pct <= 100),
                    ADD CONSTRAINT ck_rfq_suppliers_response CHECK (NOT (responded AND declined));
                ALTER TABLE purchasing.rfq_lines
                    ADD CONSTRAINT ck_rfq_lines_quantity CHECK (quantity > 0),
                    ADD CONSTRAINT ck_rfq_lines_reason CHECK (selection_reason IS NULL OR selection_reason IN ('LeadTime', 'Quality', 'PaymentTerm', 'ApprovedSupplier', 'Other'));
                ALTER TABLE purchasing.rfq_line_requests
                    ADD CONSTRAINT ck_rfq_line_requests_quantity CHECK (quantity > 0);
                ALTER TABLE purchasing.rfq_quotes
                    ADD CONSTRAINT ck_rfq_quotes_values CHECK (unit_price > 0 AND available_quantity > 0 AND lead_days >= 0 AND lead_days <= 365);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rfq_line_requests",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "rfq_quotes",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "rfq_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "rfq_suppliers",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "rfqs",
                schema: "purchasing");
        }
    }
}
