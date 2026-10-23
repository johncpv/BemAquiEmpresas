using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchasingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.CreateTable(
                name: "parameters",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchasing_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_requests",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    run_date = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_request_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    entered_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    needed_by = table.Column<DateOnly>(type: "date", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bom_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    manually_edited = table.Column<bool>(type: "boolean", nullable: false),
                    calculated_on_onboarding = table.Column<bool>(type: "boolean", nullable: false),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_request_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_request_lines_purchase_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchasing_parameters_key",
                schema: "purchasing",
                table: "parameters",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_lines_item_status",
                schema: "purchasing",
                table: "purchase_request_lines",
                columns: new[] { "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_lines_production_order_id",
                schema: "purchasing",
                table: "purchase_request_lines",
                column: "production_order_id",
                filter: "production_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_request_lines_open_min_stock",
                schema: "purchasing",
                table: "purchase_request_lines",
                column: "item_id",
                unique: true,
                filter: "status = 'Open' AND origin = 'MinStock' AND NOT manually_edited");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_request_lines_open_shortage",
                schema: "purchasing",
                table: "purchase_request_lines",
                columns: new[] { "production_order_id", "item_id" },
                unique: true,
                filter: "status = 'Open' AND origin = 'BomShortage' AND NOT manually_edited");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_request_lines_request_sequence",
                schema: "purchasing",
                table: "purchase_request_lines",
                columns: new[] { "request_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_production_order_id",
                schema: "purchasing",
                table: "purchase_requests",
                column: "production_order_id",
                filter: "production_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_project_id",
                schema: "purchasing",
                table: "purchase_requests",
                column: "project_id",
                filter: "project_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_status_origin",
                schema: "purchasing",
                table: "purchase_requests",
                columns: new[] { "status", "origin" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_requests_number",
                schema: "purchasing",
                table: "purchase_requests",
                column: "number",
                unique: true);

            // CMP-01: origem e situação fechadas, quantidades positivas (a linha nunca é negativa) e o pedido nunca acima da linha.
            migrationBuilder.Sql("""
                ALTER TABLE purchasing.purchase_requests
                    ADD CONSTRAINT ck_purchase_requests_origin CHECK (origin IN ('Manual', 'BomShortage', 'MinStock')),
                    ADD CONSTRAINT ck_purchase_requests_status CHECK (status IN ('Open', 'Quoting', 'Ordered', 'Cancelled'));
                ALTER TABLE purchasing.purchase_request_lines
                    ADD CONSTRAINT ck_purchase_request_lines_origin CHECK (origin IN ('Manual', 'BomShortage', 'MinStock')),
                    ADD CONSTRAINT ck_purchase_request_lines_status CHECK (status IN ('Open', 'Quoting', 'Ordered', 'Cancelled')),
                    ADD CONSTRAINT ck_purchase_request_lines_quantities CHECK (quantity > 0 AND ordered_quantity >= 0 AND ordered_quantity <= quantity
                        AND (entered_quantity IS NULL OR entered_quantity > 0));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parameters",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_request_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_requests",
                schema: "purchasing");
        }
    }
}
