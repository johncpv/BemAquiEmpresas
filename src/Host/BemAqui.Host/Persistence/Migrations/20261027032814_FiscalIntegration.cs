using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.CreateTable(
                name: "integration_settings",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    base_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    api_key_cipher = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    api_key_set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    emitter_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emitter_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issuer_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    certificate_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_test_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_settings", x => x.id);
                    table.CheckConstraint("ck_integration_settings_slot", "slot = 1");
                });

            migrationBuilder.CreateTable(
                name: "outgoing_invoices",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    emitter_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    series = table.Column<int>(type: "integer", nullable: true),
                    number = table.Column<long>(type: "bigint", nullable: true),
                    protocol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_code = table.Column<int>(type: "integer", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suggested_action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contingency_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xml_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xml_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_invoices", x => x.id);
                    table.CheckConstraint("ck_outgoing_invoices_attempt", "attempt >= 1");
                    table.CheckConstraint("ck_outgoing_invoices_payload_hash", "payload_hash IS NULL OR length(payload_hash) = 64");
                });

            migrationBuilder.CreateIndex(
                name: "ux_integration_settings_slot",
                schema: "fiscal",
                table: "integration_settings",
                column: "slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_status_processing_since",
                schema: "fiscal",
                table: "outgoing_invoices",
                columns: new[] { "status", "processing_since" });

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoices_access_key",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "access_key",
                unique: true,
                filter: "access_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoices_emitter_document_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "emitter_document_id",
                unique: true,
                filter: "emitter_document_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_settings",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "outgoing_invoices",
                schema: "fiscal");
        }
    }
}
