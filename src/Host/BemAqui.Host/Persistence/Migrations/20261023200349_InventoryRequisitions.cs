using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_requisitions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    step_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    over_bom_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reversal_self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_requisitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_requisition_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    over_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    over_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_extra = table.Column<bool>(type: "boolean", nullable: false),
                    returns_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_requisition_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_material_requisition_lines_retur",
                        column: x => x.returns_line_id,
                        principalSchema: "inventory",
                        principalTable: "material_requisition_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_material_requisitions_requisitio",
                        column: x => x.requisition_id,
                        principalSchema: "inventory",
                        principalTable: "material_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_material_requisition_lines_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "inventory",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_item_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_location_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_lot_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_movement_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "movement_id",
                filter: "movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_reservation_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisition_lines_returns_line_id",
                schema: "inventory",
                table: "material_requisition_lines",
                column: "returns_line_id",
                filter: "returns_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_material_requisition_lines_requisition_sequence",
                schema: "inventory",
                table: "material_requisition_lines",
                columns: new[] { "requisition_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_material_requisitions_order_requested_at",
                schema: "inventory",
                table: "material_requisitions",
                columns: new[] { "production_order_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_material_requisitions_project_id",
                schema: "inventory",
                table: "material_requisitions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_requisitions_status_requested_at",
                schema: "inventory",
                table: "material_requisitions",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_material_requisitions_number",
                schema: "inventory",
                table: "material_requisitions",
                column: "number",
                unique: true);

            // EST-03: quantidades sempre positivas, excedente entre 0 e a quantidade, tipo e situação conhecidos.
            migrationBuilder.Sql(
                """
                ALTER TABLE inventory.material_requisition_lines
                    ADD CONSTRAINT ck_material_requisition_lines_quantity CHECK (quantity > 0 AND over_quantity >= 0 AND over_quantity <= quantity);
                ALTER TABLE inventory.material_requisitions
                    ADD CONSTRAINT ck_material_requisitions_kind CHECK (kind IN ('Issue', 'Return', 'Loss')),
                    ADD CONSTRAINT ck_material_requisitions_status CHECK (status IN ('Requested', 'PendingApproval', 'Completed', 'Rejected', 'Reversed')),
                    ADD CONSTRAINT ck_material_requisitions_over_bom_value CHECK (over_bom_value >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_requisition_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "material_requisitions",
                schema: "inventory");
        }
    }
}
