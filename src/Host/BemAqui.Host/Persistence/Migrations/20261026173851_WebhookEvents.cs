using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_events",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(208)", maxLength: 208, nullable: false),
                    body_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requeues = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_tokens",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    previous_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    secret_cipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    previous_secret_cipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    previous_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_pending",
                schema: "shared",
                table: "webhook_events",
                column: "status",
                filter: "status IN ('Received','Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_received_at",
                schema: "shared",
                table: "webhook_events",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_events_provider_body_sha256",
                schema: "shared",
                table: "webhook_events",
                columns: new[] { "provider", "body_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_webhook_events_provider_external_id",
                schema: "shared",
                table: "webhook_events",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_tokens_provider_scope_id",
                schema: "shared",
                table: "webhook_tokens",
                columns: new[] { "provider", "scope_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_events",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "webhook_tokens",
                schema: "shared");
        }
    }
}
