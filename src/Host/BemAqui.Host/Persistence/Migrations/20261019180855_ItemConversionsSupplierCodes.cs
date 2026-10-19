using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemConversionsSupplierCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_supplier_codes",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    supplier_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_supplier_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_supplier_codes_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_unit_conversions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_unit_conversions", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_unit_conversions_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_unit_conversions_units_from_unit_id",
                        column: x => x.from_unit_id,
                        principalSchema: "inventory",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_unit_conversions_units_to_unit_id",
                        column: x => x.to_unit_id,
                        principalSchema: "inventory",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_supplier_codes_item_id",
                schema: "inventory",
                table: "item_supplier_codes",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_supplier_codes_party_code",
                schema: "inventory",
                table: "item_supplier_codes",
                columns: new[] { "party_id", "supplier_code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_item_unit_conversions_from_unit_id",
                schema: "inventory",
                table: "item_unit_conversions",
                column: "from_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_unit_conversions_pair",
                schema: "inventory",
                table: "item_unit_conversions",
                columns: new[] { "item_id", "from_unit_id", "to_unit_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_item_unit_conversions_to_unit_id",
                schema: "inventory",
                table: "item_unit_conversions",
                column: "to_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_supplier_codes",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "item_unit_conversions",
                schema: "inventory");
        }
    }
}
