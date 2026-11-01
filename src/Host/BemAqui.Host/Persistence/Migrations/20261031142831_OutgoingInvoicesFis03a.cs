using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingInvoicesFis03a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "additional_information",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "authorization_published_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calculation_error",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "carrier_party_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_tax_amount",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "danfe_attachment_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discarded_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "discarded_by",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_cc",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "emailed_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "environment",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Homologation");

            migrationBuilder.AddColumn<bool>(
                name: "final_consumer",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "freight",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "freight_mode",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_weight",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issued_by",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issued_on",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nature_of_operation",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "OwnProductionSale");

            migrationBuilder.AddColumn<decimal>(
                name: "other_expenses",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_city_code",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_city_name",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_complement",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_district",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_document",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_email",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_name",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_number",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_phone",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_postal_code",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_state",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_state_registration",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_street",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_type",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Contributor");

            migrationBuilder.AddColumn<bool>(
                name: "send_to_customer",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "shipment_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipment_number",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "totals",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"products\":0,\"discount\":0,\"freight\":0,\"otherExpenses\":0,\"taxes\":[],\"invoiceTotal\":0,\"costTax\":0}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "NFe");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "volume_species",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "volumes",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "project_tax_cutover_on",
                schema: "fiscal",
                table: "fiscal_settings",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "number_gaps",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_gaps", x => x.id);
                    table.CheckConstraint("ck_number_gaps_reason", "reason IN ('Rejected', 'Denied')");
                });

            migrationBuilder.CreateTable(
                name: "outgoing_invoice_items",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    shipment_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    cest = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    unit = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    quote_item_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    operation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    shipped_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    other_expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ipi_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    additional_info = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    serial_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    serial_numbers = table.Column<string[]>(type: "text[]", nullable: false),
                    chosen_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_rule_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_rule_version = table.Column<int>(type: "integer", nullable: true),
                    tax_rule_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    ibs_cbs_classification = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    ipi_framework = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    taxes = table.Column<string>(type: "jsonb", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_invoice_items", x => x.id);
                    table.CheckConstraint("ck_outgoing_invoice_items_quantity", "quantity >= 0");
                    table.CheckConstraint("ck_outgoing_invoice_items_values", "unit_price >= 0 AND discount >= 0 AND freight >= 0 AND other_expenses >= 0");
                    table.ForeignKey(
                        name: "fk_outgoing_invoice_items_outgoing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "fiscal",
                        principalTable: "outgoing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_invoice_receivables",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_invoice_receivables", x => x.id);
                    table.ForeignKey(
                        name: "fk_outgoing_invoice_receivables_outgoing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "fiscal",
                        principalTable: "outgoing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_created_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_party_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_project_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_status_issued_on",
                schema: "fiscal",
                table: "outgoing_invoices",
                columns: new[] { "status", "issued_on" });

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoices_active_shipment",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "shipment_id",
                unique: true,
                filter: "shipment_id IS NOT NULL AND discarded_at IS NULL AND status NOT IN ('Cancelled', 'Rejected', 'Denied')");

            migrationBuilder.CreateIndex(
                name: "ix_number_gaps_invoice_id",
                schema: "fiscal",
                table: "number_gaps",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ux_number_gaps_series_number",
                schema: "fiscal",
                table: "number_gaps",
                columns: new[] { "series", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoice_items_sequence",
                schema: "fiscal",
                table: "outgoing_invoice_items",
                columns: new[] { "invoice_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoice_receivables_receivable_id",
                schema: "fiscal",
                table: "outgoing_invoice_receivables",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoice_receivables",
                schema: "fiscal",
                table: "outgoing_invoice_receivables",
                columns: new[] { "invoice_id", "receivable_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_gaps",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "outgoing_invoice_items",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "outgoing_invoice_receivables",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_created_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_party_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_project_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_status_issued_on",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ux_outgoing_invoices_active_shipment",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "additional_information",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "authorization_published_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "calculation_error",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "carrier_party_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "contract_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cost_tax_amount",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "danfe_attachment_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "discarded_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "discarded_by",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "email_cc",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "emailed_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "environment",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "final_consumer",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "freight",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "freight_mode",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "gross_weight",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "issued_by",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "issued_on",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "nature_of_operation",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "operation",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "other_expenses",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "party_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_city_code",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_city_name",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_complement",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_district",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_document",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_email",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_name",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_number",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_phone",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_postal_code",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_state",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_state_registration",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_street",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "recipient_type",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "send_to_customer",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "shipment_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "shipment_number",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "totals",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "volume_species",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "volumes",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "project_tax_cutover_on",
                schema: "fiscal",
                table: "fiscal_settings");
        }
    }
}
