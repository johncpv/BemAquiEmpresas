using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringRoutingTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engineering");

            migrationBuilder.CreateTable(
                name: "routing_templates",
                schema: "engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    product_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "routing_template_items",
                schema: "engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_routing_template_items_routing_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "engineering",
                        principalTable: "routing_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "routing_template_steps",
                schema: "engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    standard_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    is_quality_step = table.Column<bool>(type: "boolean", nullable: false),
                    checklist = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_template_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_routing_template_steps_routing_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "engineering",
                        principalTable: "routing_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_routing_template_items_template_id",
                schema: "engineering",
                table: "routing_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ux_routing_template_items_item",
                schema: "engineering",
                table: "routing_template_items",
                column: "item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_routing_template_steps_template_id_sequence",
                schema: "engineering",
                table: "routing_template_steps",
                columns: new[] { "template_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_routing_template_steps_work_center_id",
                schema: "engineering",
                table: "routing_template_steps",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ux_routing_templates_seed_key",
                schema: "engineering",
                table: "routing_templates",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");

            // ENG-01 (D10): a letra é única no roteiro. DEFERRABLE para trocar duas letras de lugar num salvar só (o UPDATE
            // de cada etapa sai em ordem qualquer); fica fora do modelo do EF, que não expressa constraint adiável.
            migrationBuilder.Sql("ALTER TABLE engineering.routing_template_steps ADD CONSTRAINT ux_routing_template_steps_code UNIQUE (template_id, code) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routing_template_items",
                schema: "engineering");

            migrationBuilder.DropTable(
                name: "routing_template_steps",
                schema: "engineering");

            migrationBuilder.DropTable(
                name: "routing_templates",
                schema: "engineering");
        }
    }
}
