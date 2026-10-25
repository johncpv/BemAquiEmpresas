using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planning_queue",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_queue", x => x.id);
                    table.CheckConstraint("ck_planning_queue_position", "position > 0");
                });

            migrationBuilder.CreateTable(
                name: "planning_queue_changes",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    moves = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_queue_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planning_queue_state",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reordered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_queue_state", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planning_queue_position",
                schema: "production",
                table: "planning_queue",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ix_planning_queue_project_id",
                schema: "production",
                table: "planning_queue",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_queue_changes_at",
                schema: "production",
                table: "planning_queue_changes",
                column: "at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planning_queue",
                schema: "production");

            migrationBuilder.DropTable(
                name: "planning_queue_changes",
                schema: "production");

            migrationBuilder.DropTable(
                name: "planning_queue_state",
                schema: "production");
        }
    }
}
