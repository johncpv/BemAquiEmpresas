using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboundWebhooksInt02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbound_webhook_attempts",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_webhook_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_webhook_deliveries",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_webhook_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_webhook_events",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_webhook_subscriptions",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    events = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    secret_cipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    previous_secret_cipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    previous_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pending_secret_delivery = table.Column<bool>(type: "boolean", nullable: false),
                    secret_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rotation_day = table.Column<DateOnly>(type: "date", nullable: true),
                    rotations_on_day = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_webhook_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_attempts_delivery_id_number",
                schema: "shared",
                table: "outbound_webhook_attempts",
                columns: new[] { "delivery_id", "number" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_deliveries_event_id_subscription_id",
                schema: "shared",
                table: "outbound_webhook_deliveries",
                columns: new[] { "event_id", "subscription_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_deliveries_open",
                schema: "shared",
                table: "outbound_webhook_deliveries",
                column: "status",
                filter: "status IN ('Pending','Retrying')");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_deliveries_subscription_id_status_created_",
                schema: "shared",
                table: "outbound_webhook_deliveries",
                columns: new[] { "subscription_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_events_occurred_at",
                schema: "shared",
                table: "outbound_webhook_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_subscriptions_connection_id",
                schema: "shared",
                table: "outbound_webhook_subscriptions",
                column: "connection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_webhook_subscriptions_status",
                schema: "shared",
                table: "outbound_webhook_subscriptions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_webhook_attempts",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "outbound_webhook_deliveries",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "outbound_webhook_events",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "outbound_webhook_subscriptions",
                schema: "shared");
        }
    }
}
