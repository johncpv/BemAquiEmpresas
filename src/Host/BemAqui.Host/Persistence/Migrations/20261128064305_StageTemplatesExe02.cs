using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StageTemplatesExe02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stage_templates",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    segment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    suggested_milestones = table.Column<string>(type: "jsonb", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stage_template_items",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_stage_template_items_stage_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "commercial",
                        principalTable: "stage_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stage_template_steps",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    role = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    standard_hours_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    checklist = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_template_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_stage_template_steps_stage_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "commercial",
                        principalTable: "stage_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stage_template_items_template_id",
                schema: "commercial",
                table: "stage_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ux_stage_template_items_item",
                schema: "commercial",
                table: "stage_template_items",
                column: "item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stage_template_steps_position_id",
                schema: "commercial",
                table: "stage_template_steps",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_template_steps_template_id_sequence",
                schema: "commercial",
                table: "stage_template_steps",
                columns: new[] { "template_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_stage_templates_seed_key",
                schema: "commercial",
                table: "stage_templates",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");

            // EXE-02 CA 9 e 12: só CREATE (tabelas, índices e constraints novas); nenhuma linha existente é tocada.
            // Nome único entre os ativos e não excluídos, sem diferenciar maiúsculas (CA 2).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_stage_templates_name_active ON commercial.stage_templates (lower(name)) WHERE is_active AND NOT is_deleted;");
            // Um padrão por segmento (nulo = do tenant) entre os não excluídos (CA 7 e 9).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_stage_templates_default_segment ON commercial.stage_templates ((coalesce(segment, ''))) WHERE is_default AND NOT is_deleted;");
            // A letra é única no modelo. DEFERRABLE para trocar duas letras de lugar num salvar só (padrão do roteiro, ENG-01 D10).
            migrationBuilder.Sql("ALTER TABLE commercial.stage_template_steps ADD CONSTRAINT ux_stage_template_steps_code UNIQUE (template_id, code) DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE commercial.stage_template_steps ADD CONSTRAINT ck_stage_template_steps_role CHECK (role IN ('Execution', 'Delivery', 'Acceptance'));");
            migrationBuilder.Sql("ALTER TABLE commercial.stage_template_steps ADD CONSTRAINT ck_stage_template_steps_hours CHECK (standard_hours_per_unit >= 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stage_template_items",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "stage_template_steps",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "stage_templates",
                schema: "commercial");
        }
    }
}
