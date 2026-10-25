using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "shipment_id",
                schema: "inventory",
                table: "serials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shipped_at",
                schema: "inventory",
                table: "serials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    carrier_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_plate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    volumes = table.Column<int>(type: "integer", nullable: true),
                    gross_weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stock_approval_requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_approval_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stock_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pdf_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.CheckConstraint("ck_shipments_gross_weight", "gross_weight IS NULL OR gross_weight >= 0");
                    table.CheckConstraint("ck_shipments_volumes", "volumes IS NULL OR volumes > 0");
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    from_general_stock = table.Column<bool>(type: "boolean", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_lines", x => x.id);
                    table.CheckConstraint("ck_shipment_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_shipment_lines_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "production",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_line_serials",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_line_serials", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_line_serials_shipment_lines_line_id",
                        column: x => x.line_id,
                        principalSchema: "production",
                        principalTable: "shipment_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_line_serials_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "production",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_line_serials_line_id",
                schema: "production",
                table: "shipment_line_serials",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_line_serials_shipment_id",
                schema: "production",
                table: "shipment_line_serials",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ux_shipment_line_serials_active",
                schema: "production",
                table: "shipment_line_serials",
                column: "serial_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_item_id",
                schema: "production",
                table: "shipment_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_lines_shipment_id_sequence",
                schema: "production",
                table: "shipment_lines",
                columns: new[] { "shipment_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_number",
                schema: "production",
                table: "shipments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_party_id_created_at",
                schema: "production",
                table: "shipments",
                columns: new[] { "party_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_project_id_status",
                schema: "production",
                table: "shipments",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status",
                schema: "production",
                table: "shipments",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipment_line_serials",
                schema: "production");

            migrationBuilder.DropTable(
                name: "shipment_lines",
                schema: "production");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "production");

            migrationBuilder.DropColumn(
                name: "shipment_id",
                schema: "inventory",
                table: "serials");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                schema: "inventory",
                table: "serials");
        }
    }
}
