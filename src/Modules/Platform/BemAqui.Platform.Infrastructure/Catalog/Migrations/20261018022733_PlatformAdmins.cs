using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "action",
                schema: "audit",
                table: "audit_log",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.CreateTable(
                name: "platform_admins",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    totp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    totp_secret_cipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    totp_last_step = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_admins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_admin_tokens",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_admin_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_admin_tokens_platform_admins_admin_id",
                        column: x => x.admin_id,
                        principalSchema: "catalog",
                        principalTable: "platform_admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_recovery_codes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_recovery_codes_platform_admins_admin_id",
                        column: x => x.admin_id,
                        principalSchema: "catalog",
                        principalTable: "platform_admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_sessions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_sessions_platform_admins_admin_id",
                        column: x => x.admin_id,
                        principalSchema: "catalog",
                        principalTable: "platform_admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_tokens_admin_id",
                schema: "catalog",
                table: "platform_admin_tokens",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_tokens_token_hash",
                schema: "catalog",
                table: "platform_admin_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_admins_normalized_email",
                schema: "catalog",
                table: "platform_admins",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_recovery_codes_admin_id_code_hash",
                schema: "catalog",
                table: "platform_recovery_codes",
                columns: new[] { "admin_id", "code_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_admin_id",
                schema: "catalog",
                table: "platform_sessions",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_token_hash",
                schema: "catalog",
                table: "platform_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_admin_tokens",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_recovery_codes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_sessions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_admins",
                schema: "catalog");

            migrationBuilder.AlterColumn<string>(
                name: "action",
                schema: "audit",
                table: "audit_log",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
