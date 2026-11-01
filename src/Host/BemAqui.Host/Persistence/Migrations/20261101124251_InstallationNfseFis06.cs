using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallationNfseFis06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "installation_order_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "installation_order_number",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "iss_typed",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "linked_invoice_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Emitter");

            migrationBuilder.AddColumn<string>(
                name: "portal_cancellation",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verification_code",
                schema: "fiscal",
                table: "outgoing_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "installation_billings",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_billings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_invoices_linked_invoice_id",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "linked_invoice_id",
                filter: "linked_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoices_active_installation",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "installation_order_id",
                unique: true,
                filter: "installation_order_id IS NOT NULL AND discarded_at IS NULL AND status NOT IN ('Cancelled', 'Rejected', 'Denied')");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_invoices_nfse_number",
                schema: "fiscal",
                table: "outgoing_invoices",
                column: "number",
                unique: true,
                filter: "type = 'NFSe' AND number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_installation_billings_project_id",
                schema: "fiscal",
                table: "installation_billings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_installation_billings_order",
                schema: "fiscal",
                table: "installation_billings",
                column: "installation_order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_billings",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_invoices_linked_invoice_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ux_outgoing_invoices_active_installation",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropIndex(
                name: "ux_outgoing_invoices_nfse_number",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "installation_order_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "installation_order_number",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "iss_typed",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "linked_invoice_id",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "portal_cancellation",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "service",
                schema: "fiscal",
                table: "outgoing_invoices");

            migrationBuilder.DropColumn(
                name: "verification_code",
                schema: "fiscal",
                table: "outgoing_invoices");
        }
    }
}
