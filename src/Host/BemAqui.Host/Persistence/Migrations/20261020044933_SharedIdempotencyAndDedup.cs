using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedIdempotencyAndDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedup_key",
                schema: "shared",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "shared",
                columns: table => new
                {
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    body_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ux_notifications_user_dedup",
                schema: "shared",
                table: "notifications",
                columns: new[] { "user_id", "dedup_key" },
                unique: true,
                filter: "dedup_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_created_at",
                schema: "shared",
                table: "idempotency_records",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "shared");

            migrationBuilder.DropIndex(
                name: "ux_notifications_user_dedup",
                schema: "shared",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dedup_key",
                schema: "shared",
                table: "notifications");
        }
    }
}
