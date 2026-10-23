using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reservations_backfilled_at",
                schema: "inventory",
                table: "parameters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bom_revision = table.Column<int>(type: "integer", nullable: false),
                    bom_released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_issued = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservations_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservations_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_changes",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    requested_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    shortage_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    origin = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservation_changes_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "inventory",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_changes_item_at",
                schema: "inventory",
                table: "reservation_changes",
                columns: new[] { "item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_changes_order_at",
                schema: "inventory",
                table: "reservation_changes",
                columns: new[] { "production_order_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_changes_reservation_id",
                schema: "inventory",
                table: "reservation_changes",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_item_status",
                schema: "inventory",
                table: "reservations",
                columns: new[] { "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reservations_location_id",
                schema: "inventory",
                table: "reservations",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_order_item_location",
                schema: "inventory",
                table: "reservations",
                columns: new[] { "production_order_id", "item_id", "location_id" },
                unique: true);

            // EST-02 (ADR 0023 §2): quantidades da reserva nunca negativas; o reservado efetivo é o da posição, que já tem
            // CHECK (quantity_reserved BETWEEN 0 AND quantity_on_hand) desde a InventoryStockEngine.
            migrationBuilder.Sql("""
                ALTER TABLE inventory.reservations
                    ADD CONSTRAINT ck_reservations_quantities CHECK (requested_quantity >= 0 AND quantity >= 0 AND quantity_issued >= 0),
                    ADD CONSTRAINT ck_reservations_status CHECK (status IN ('Active', 'Consumed', 'Released'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservation_changes",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "reservations",
                schema: "inventory");

            migrationBuilder.DropColumn(
                name: "reservations_backfilled_at",
                schema: "inventory",
                table: "parameters");
        }
    }
}
