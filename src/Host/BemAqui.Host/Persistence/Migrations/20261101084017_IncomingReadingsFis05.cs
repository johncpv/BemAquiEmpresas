using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncomingReadingsFis05 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replaced_by_xml_at",
                schema: "fiscal",
                table: "incoming_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_xml_by",
                schema: "fiscal",
                table: "incoming_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "xml_differences",
                schema: "fiscal",
                table: "incoming_invoices",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "incoming_invoice_readings",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    pdf_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pages = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    access_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    corrected_fields = table.Column<string>(type: "jsonb", nullable: false),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xml_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xml_requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoming_invoice_readings", x => x.id);
                    table.CheckConstraint("ck_incoming_invoice_readings_access_key", "access_key IS NULL OR length(access_key) = 44");
                });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_invoice_readings_access_key",
                schema: "fiscal",
                table: "incoming_invoice_readings",
                column: "access_key",
                filter: "access_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_incoming_invoice_readings_status_created_at",
                schema: "fiscal",
                table: "incoming_invoice_readings",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_incoming_invoice_readings_invoice_id",
                schema: "fiscal",
                table: "incoming_invoice_readings",
                column: "invoice_id",
                unique: true,
                filter: "invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incoming_invoice_readings",
                schema: "fiscal");

            migrationBuilder.DropColumn(
                name: "replaced_by_xml_at",
                schema: "fiscal",
                table: "incoming_invoices");

            migrationBuilder.DropColumn(
                name: "replaced_by_xml_by",
                schema: "fiscal",
                table: "incoming_invoices");

            migrationBuilder.DropColumn(
                name: "xml_differences",
                schema: "fiscal",
                table: "incoming_invoices");
        }
    }
}
