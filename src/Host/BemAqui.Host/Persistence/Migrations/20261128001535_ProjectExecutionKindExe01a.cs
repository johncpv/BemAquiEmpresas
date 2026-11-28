using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectExecutionKindExe01a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "accepted_on",
                schema: "commercial",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delivered_by",
                schema: "commercial",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "execution_kind",
                schema: "commercial",
                table: "projects",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Fabrication");

            migrationBuilder.AddColumn<string>(
                name: "execution_kind_source",
                schema: "commercial",
                table: "projects",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<DateOnly>(
                name: "fabrication_compat_on",
                schema: "commercial",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "started_on",
                schema: "commercial",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revert_note",
                schema: "finance",
                table: "project_trigger_events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reverted",
                schema: "finance",
                table: "project_trigger_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "finance",
                table: "project_trigger_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "project_acceptances",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    accepted_by_role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    backdate_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reverted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reverted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revert_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_acceptances_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "commercial",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_execution_kind_source_default",
                schema: "commercial",
                table: "projects",
                column: "execution_kind_source",
                filter: "execution_kind_source = 'Default'");

            migrationBuilder.CreateIndex(
                name: "ix_project_acceptances_project_id_active",
                schema: "commercial",
                table: "project_acceptances",
                column: "project_id",
                unique: true,
                filter: "reverted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_acceptances_project_id_version",
                schema: "commercial",
                table: "project_acceptances",
                columns: new[] { "project_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_acceptances",
                schema: "commercial");

            migrationBuilder.DropIndex(
                name: "ix_projects_execution_kind_source_default",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "accepted_on",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "delivered_by",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "execution_kind",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "execution_kind_source",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "fabrication_compat_on",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "started_on",
                schema: "commercial",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "revert_note",
                schema: "finance",
                table: "project_trigger_events");

            migrationBuilder.DropColumn(
                name: "reverted",
                schema: "finance",
                table: "project_trigger_events");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "finance",
                table: "project_trigger_events");
        }
    }
}
