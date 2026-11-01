using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceEventsFis03b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_protocol",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancellation_published_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancellation_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_danfe_attachment_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cancelled_on",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "correction_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invalidation_event_id",
                schema: "fiscal",
                table: "number_gaps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "fiscal",
                table: "number_gaps",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "fiscal",
                table: "number_gaps",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "cancellation_requests",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cancellation_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_cancellation_requests_outgoing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "fiscal",
                        principalTable: "outgoing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_events",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    protocol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    emitter_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xml_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_code = table.Column<int>(type: "integer", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    series = table.Column<int>(type: "integer", nullable: true),
                    first_number = table.Column<long>(type: "bigint", nullable: true),
                    last_number = table.Column<long>(type: "bigint", nullable: true),
                    cancellation_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notify_customer = table.Column<bool>(type: "boolean", nullable: false),
                    emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_events", x => x.id);
                    table.CheckConstraint("ck_fiscal_events_sequence", "sequence >= 1");
                    table.CheckConstraint("ck_fiscal_events_target", "invoice_id IS NOT NULL OR (series IS NOT NULL AND first_number IS NOT NULL AND last_number IS NOT NULL AND first_number <= last_number)");
                    table.ForeignKey(
                        name: "fk_fiscal_events_outgoing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "fiscal",
                        principalTable: "outgoing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_cancellation_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "cancellation_sent_at",
                filter: "cancellation_sent_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_number_gaps_status_created_at",
                schema: "fiscal",
                table: "number_gaps",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cancellation_requests_status_expires_at",
                schema: "fiscal",
                table: "cancellation_requests",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_cancellation_requests_pending_invoice",
                schema: "fiscal",
                table: "cancellation_requests",
                column: "invoice_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_events_status_requested_at",
                schema: "fiscal",
                table: "fiscal_events",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_events_invoice_type_sequence",
                schema: "fiscal",
                table: "fiscal_events",
                columns: new[] { "invoice_id", "type", "sequence" },
                unique: true,
                filter: "invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cancellation_requests",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_events",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_cancellation_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ix_number_gaps_status_created_at",
                schema: "fiscal",
                table: "number_gaps");

            migrationBuilder.DropColumn(
                name: "cancellation_protocol",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancellation_published_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancellation_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_danfe_attachment_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "cancelled_on",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "correction_sent_at",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "invalidation_event_id",
                schema: "fiscal",
                table: "number_gaps");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "fiscal",
                table: "number_gaps");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "fiscal",
                table: "number_gaps");
        }
    }
}
