using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetMovementsPat02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_movements",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moved_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    disposal_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    disposal_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    buyer_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_movements", x => x.id);
                    table.CheckConstraint("ck_asset_movements_disposal_value", "disposal_value IS NULL OR disposal_value >= 0");
                    table.CheckConstraint("ck_asset_movements_kind", "kind IN ('Transfer', 'Disposal', 'Reactivation')");
                    table.ForeignKey(
                        name: "fk_asset_movements_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_movements_asset_id_moved_on",
                schema: "assets",
                table: "asset_movements",
                columns: new[] { "asset_id", "moved_on", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_movements_buyer_party_id",
                schema: "assets",
                table: "asset_movements",
                column: "buyer_party_id",
                filter: "buyer_party_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_movements_from_employee_id",
                schema: "assets",
                table: "asset_movements",
                column: "from_employee_id",
                filter: "from_employee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_movements_to_employee_id",
                schema: "assets",
                table: "asset_movements",
                column: "to_employee_id",
                filter: "to_employee_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_movements",
                schema: "assets");
        }
    }
}
