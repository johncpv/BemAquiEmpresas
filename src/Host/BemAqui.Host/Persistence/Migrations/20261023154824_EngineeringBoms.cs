using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringBoms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boms",
                schema: "engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    copied_from_bom_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bom_lines",
                schema: "engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scrap_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    required_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bom_lines_boms_bom_id",
                        column: x => x.bom_id,
                        principalSchema: "engineering",
                        principalTable: "boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_item_id",
                schema: "engineering",
                table: "bom_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ux_bom_lines_item",
                schema: "engineering",
                table: "bom_lines",
                columns: new[] { "bom_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boms_copied_from_bom_id",
                schema: "engineering",
                table: "boms",
                column: "copied_from_bom_id");

            migrationBuilder.CreateIndex(
                name: "ux_boms_draft",
                schema: "engineering",
                table: "boms",
                column: "production_order_id",
                unique: true,
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ux_boms_order_revision",
                schema: "engineering",
                table: "boms",
                columns: new[] { "production_order_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_boms_released",
                schema: "engineering",
                table: "boms",
                column: "production_order_id",
                unique: true,
                filter: "status = 'Released'");

            // ENG-02: situação conhecida, liberada sempre com quem e quando, quantidade > 0 e perda entre 0 e 100% (o serviço também valida).
            migrationBuilder.Sql("""
                ALTER TABLE engineering.boms ADD CONSTRAINT ck_boms_status CHECK (status IN ('Draft', 'Released', 'Superseded'));
                ALTER TABLE engineering.boms ADD CONSTRAINT ck_boms_released CHECK (status = 'Draft' OR released_at IS NOT NULL);
                ALTER TABLE engineering.boms ADD CONSTRAINT ck_boms_revision CHECK (revision >= 0);
                ALTER TABLE engineering.bom_lines ADD CONSTRAINT ck_bom_lines_quantity CHECK (quantity > 0);
                ALTER TABLE engineering.bom_lines ADD CONSTRAINT ck_bom_lines_scrap_pct CHECK (scrap_pct >= 0 AND scrap_pct <= 100);
                ALTER TABLE engineering.bom_lines ADD CONSTRAINT ck_bom_lines_required CHECK (required_quantity >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_lines",
                schema: "engineering");

            migrationBuilder.DropTable(
                name: "boms",
                schema: "engineering");
        }
    }
}
