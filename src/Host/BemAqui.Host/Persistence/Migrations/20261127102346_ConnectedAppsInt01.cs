using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectedAppsInt01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connected_apps",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    required_module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connected_apps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_authorization_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    code_challenge = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    account_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_authorization_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_authorization_codes_connected_apps_app_id",
                        column: x => x.app_id,
                        principalSchema: "identity",
                        principalTable: "connected_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_connect_intents",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_connect_intents", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_connect_intents_connected_apps_app_id",
                        column: x => x.app_id,
                        principalSchema: "identity",
                        principalTable: "connected_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_connections",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    account_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reconsent_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    module_off_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_connections_connected_apps_app_id",
                        column: x => x.app_id,
                        principalSchema: "identity",
                        principalTable: "connected_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_tokens_app_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "identity",
                        principalTable: "app_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_authorization_codes_app_id",
                schema: "identity",
                table: "app_authorization_codes",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_authorization_codes_code_hash",
                schema: "identity",
                table: "app_authorization_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_authorization_codes_expires_at",
                schema: "identity",
                table: "app_authorization_codes",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_app_connect_intents_app_id",
                schema: "identity",
                table: "app_connect_intents",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_connect_intents_expires_at",
                schema: "identity",
                table: "app_connect_intents",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_app_connect_intents_intent_hash",
                schema: "identity",
                table: "app_connect_intents",
                column: "intent_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_connections_grantor_user_id",
                schema: "identity",
                table: "app_connections",
                column: "grantor_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_app_connections_open",
                schema: "identity",
                table: "app_connections",
                column: "app_id",
                unique: true,
                filter: "status <> 'Revoked'");

            migrationBuilder.CreateIndex(
                name: "ix_app_tokens_connection_id",
                schema: "identity",
                table: "app_tokens",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_tokens_family_id",
                schema: "identity",
                table: "app_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_tokens_token_hash",
                schema: "identity",
                table: "app_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connected_apps_client_id",
                schema: "identity",
                table: "connected_apps",
                column: "client_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_authorization_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_connect_intents",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_connections",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "connected_apps",
                schema: "identity");
        }
    }
}
