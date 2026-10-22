using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopfloorDevicesAndPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "absolute_expires_at",
                schema: "identity",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "host",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_kind",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.CreateTable(
                name: "shopfloor_devices",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_secret_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    secret_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    theme = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    pin_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pin_failures_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pin_failures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopfloor_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_pins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failures_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failures_in_window = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_pins", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_pins_shopfloor_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "identity",
                        principalTable: "shopfloor_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_pins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_pin_sessions",
                schema: "identity",
                table: "refresh_tokens",
                columns: new[] { "device_id", "user_id" },
                filter: "device_id IS NOT NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shopfloor_devices_previous_secret_hash",
                schema: "identity",
                table: "shopfloor_devices",
                column: "previous_secret_hash");

            migrationBuilder.CreateIndex(
                name: "ix_shopfloor_devices_revoked_at",
                schema: "identity",
                table: "shopfloor_devices",
                column: "revoked_at");

            migrationBuilder.CreateIndex(
                name: "ix_shopfloor_devices_secret_hash",
                schema: "identity",
                table: "shopfloor_devices",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_pins_device_id",
                schema: "identity",
                table: "user_pins",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_pins_user_id_device_id",
                schema: "identity",
                table: "user_pins",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_shopfloor_devices_device_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "device_id",
                principalSchema: "identity",
                principalTable: "shopfloor_devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_shopfloor_devices_device_id",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_pins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "shopfloor_devices",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_pin_sessions",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "absolute_expires_at",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "device_id",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "host",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "session_kind",
                schema: "identity",
                table: "refresh_tokens");
        }
    }
}
