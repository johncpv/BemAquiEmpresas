using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetsPat01a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    acquisition_date = table.Column<DateOnly>(type: "date", nullable: true),
                    acquisition_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    incoming_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_ref = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    supplier_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_assets_asset_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "assets",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_responsible_employee_id",
                schema: "assets",
                table: "assets",
                column: "responsible_employee_id",
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_assets_serial_number",
                schema: "assets",
                table: "assets",
                column: "serial_number",
                filter: "serial_number IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_assets_site_id",
                schema: "assets",
                table: "assets",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_status_type",
                schema: "assets",
                table: "assets",
                columns: new[] { "status", "type" },
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_assets_supplier_party_id",
                schema: "assets",
                table: "assets",
                column: "supplier_party_id",
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_assets_tag",
                schema: "assets",
                table: "assets",
                column: "tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_work_center_id",
                schema: "assets",
                table: "assets",
                column: "work_center_id",
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_sites_name",
                schema: "assets",
                table: "sites",
                column: "name",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_sites_seed_key",
                schema: "assets",
                table: "sites",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "assets");
        }
    }
}
