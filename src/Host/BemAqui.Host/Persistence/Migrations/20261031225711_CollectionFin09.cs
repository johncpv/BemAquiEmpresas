using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionFin09 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_notices",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    scheduled_for = table.Column<DateOnly>(type: "date", nullable: false),
                    email_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_notices_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_pauses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resumed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_pauses", x => x.id);
                    table.CheckConstraint("ck_collection_pauses_target", "(party_id IS NULL) <> (receivable_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_collection_pauses_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_rules",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    send_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    reply_to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_notices_deferred",
                schema: "finance",
                table: "collection_notices",
                column: "status",
                filter: "status = 'Deferred'");

            migrationBuilder.CreateIndex(
                name: "ix_collection_notices_email_log_id",
                schema: "finance",
                table: "collection_notices",
                column: "email_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_notices_party_id_created_at",
                schema: "finance",
                table: "collection_notices",
                columns: new[] { "party_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_collection_notices_receivable_step",
                schema: "finance",
                table: "collection_notices",
                columns: new[] { "receivable_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_pauses_party_id",
                schema: "finance",
                table: "collection_pauses",
                column: "party_id",
                filter: "resumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_collection_pauses_receivable_id",
                schema: "finance",
                table: "collection_pauses",
                column: "receivable_id",
                filter: "resumed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_notices",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "collection_pauses",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "collection_rules",
                schema: "finance");
        }
    }
}
