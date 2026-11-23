using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PdvStockShortfalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "shortfall_quantity",
                schema: "sales",
                table: "stock_postings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "stock_shortfalls",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_shortfalls", x => x.id);
                    table.CheckConstraint("ck_stock_shortfalls_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_shortfalls_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_shortfalls_locations_location_id",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_shortfalls_stock_movements_movement_id",
                        column: x => x.movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_shortfalls_stock_movements_reversal_movement_id",
                        column: x => x.reversal_movement_id,
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_shortfalls_item_id",
                schema: "inventory",
                table: "stock_shortfalls",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_shortfalls_location_item_status",
                schema: "inventory",
                table: "stock_shortfalls",
                columns: new[] { "location_id", "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_shortfalls_movement_id",
                schema: "inventory",
                table: "stock_shortfalls",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_shortfalls_open",
                schema: "inventory",
                table: "stock_shortfalls",
                column: "status",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_shortfalls_reversal_movement_id",
                schema: "inventory",
                table: "stock_shortfalls",
                column: "reversal_movement_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_shortfalls_source_item_location",
                schema: "inventory",
                table: "stock_shortfalls",
                columns: new[] { "source_type", "source_id", "item_id", "location_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_shortfalls",
                schema: "inventory");

            migrationBuilder.DropColumn(
                name: "shortfall_quantity",
                schema: "sales",
                table: "stock_postings");
        }
    }
}
