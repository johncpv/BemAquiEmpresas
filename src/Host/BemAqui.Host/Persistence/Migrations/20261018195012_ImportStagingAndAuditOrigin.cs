using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportStagingAndAuditOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "migration");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "audit",
                table: "audit_log",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_refs",
                schema: "migration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_table = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_xmin = table.Column<long>(type: "bigint", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_refs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_runs",
                schema: "migration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entities = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: true),
                    @operator = table.Column<string>(name: "operator", type: "character varying(254)", maxLength: 254, nullable: false),
                    snapshot_taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_items",
                schema: "migration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_table = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    warning = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    differences = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_items_import_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "migration",
                        principalTable: "import_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_refs_source",
                schema: "migration",
                table: "external_refs",
                columns: new[] { "source", "source_table", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_refs_target",
                schema: "migration",
                table: "external_refs",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_import_items_run_id_outcome",
                schema: "migration",
                table: "import_items",
                columns: new[] { "run_id", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_started_at",
                schema: "migration",
                table: "import_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_refs",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "import_items",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "import_runs",
                schema: "migration");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "audit",
                table: "audit_log");
        }
    }
}
