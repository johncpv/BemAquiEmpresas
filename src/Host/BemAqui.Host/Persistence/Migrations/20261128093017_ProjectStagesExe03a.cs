using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectStagesExe03a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stage_template_id",
                schema: "commercial",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage_template_name",
                schema: "commercial",
                table: "projects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stage_template_revision",
                schema: "commercial",
                table: "projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stages_applied_at",
                schema: "commercial",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stages_applied_by",
                schema: "commercial",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stages_changed_at",
                schema: "commercial",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_stages",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    role = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    planned_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    responsible_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completion_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    self_approved = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_stages", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_stages_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_stage_checklist",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    marked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_stage_checklist", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_stage_checklist_project_service_stages_stage_id",
                        column: x => x.stage_id,
                        principalSchema: "commercial",
                        principalTable: "project_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_stage_template_id",
                schema: "commercial",
                table: "projects",
                column: "stage_template_id",
                filter: "stage_template_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_stage_checklist_stage_id_sequence",
                schema: "commercial",
                table: "project_stage_checklist",
                columns: new[] { "stage_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_project_stages_project_id",
                schema: "commercial",
                table: "project_stages",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_stages_responsible_user_id",
                schema: "commercial",
                table: "project_stages",
                column: "responsible_user_id");

            // EXE-03a CA 1: só CREATE TABLE, ADD COLUMN (nuláveis) e constraints/índices novos; nenhuma linha existente é tocada.
            // Letra e sequência únicas no projeto, DEFERRABLE para reordenar ou trocar duas letras num salvar só (padrão da EXE-02).
            migrationBuilder.Sql("ALTER TABLE commercial.project_stages ADD CONSTRAINT ux_project_stages_code UNIQUE (project_id, code) DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE commercial.project_stages ADD CONSTRAINT ux_project_stages_sequence UNIQUE (project_id, sequence) DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE commercial.project_stages ADD CONSTRAINT ck_project_stages_role CHECK (role IN ('Execution', 'Delivery', 'Acceptance'));");
            migrationBuilder.Sql("ALTER TABLE commercial.project_stages ADD CONSTRAINT ck_project_stages_status CHECK (status IN ('Waiting', 'Ready', 'InProgress', 'AwaitingApproval', 'Done'));");
            migrationBuilder.Sql("ALTER TABLE commercial.project_stages ADD CONSTRAINT ck_project_stages_hours CHECK (planned_hours >= 0);");
            migrationBuilder.Sql("ALTER TABLE commercial.project_stage_checklist ADD CONSTRAINT ck_project_stage_checklist_result CHECK (result IS NULL OR result IN ('Done', 'NotApplicable'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_stage_checklist",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "project_stages",
                schema: "commercial");

            migrationBuilder.DropIndex(
                name: "ix_projects_stage_template_id",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stage_template_id",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stage_template_name",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stage_template_revision",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stages_applied_at",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stages_applied_by",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "stages_changed_at",
                schema: "commercial",
                table: "projects");
        }
    }
}
