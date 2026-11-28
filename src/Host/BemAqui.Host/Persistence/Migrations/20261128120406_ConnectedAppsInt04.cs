using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectedAppsInt04 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_set_by_app",
                schema: "parties",
                table: "parties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "phone_set_by_app",
                schema: "parties",
                table: "parties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "secret_version",
                schema: "shared",
                table: "outbound_webhook_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "source_connection_id",
                schema: "sales",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "webhook_secret_version",
                schema: "identity",
                table: "app_tokens",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_challenges",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salt = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_challenges_parties_party_id",
                        column: x => x.party_id,
                        principalSchema: "parties",
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_source_connection_id",
                schema: "sales",
                table: "orders",
                column: "source_connection_id",
                filter: "source_connection_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_identity_challenges_connection_id_created_at",
                schema: "parties",
                table: "identity_challenges",
                columns: new[] { "connection_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_challenges_party_id_created_at",
                schema: "parties",
                table: "identity_challenges",
                columns: new[] { "party_id", "created_at" });

            // INT-04 CA 6: o pedido passa a ser do aplicativo. A conexão que criou fica na trilha (source_connection_id) e source_app_id vira o
            // connected_apps.id. Se duas conexões do mesmo aplicativo usaram o mesmo externalId, só o mais novo muda (o índice único é por aplicativo);
            // os outros ficam na conexão, como estavam (nada se perde).
            migrationBuilder.Sql("""
                UPDATE sales.orders SET source_connection_id = source_app_id WHERE source_app_id IS NOT NULL AND source_connection_id IS NULL;
                WITH mapped AS (
                    SELECT o.id, c.app_id,
                           row_number() OVER (PARTITION BY o.origin, c.app_id, o.external_id ORDER BY o.created_at DESC) AS rn
                    FROM sales.orders o JOIN identity.app_connections c ON c.id = o.source_app_id)
                UPDATE sales.orders o SET source_app_id = m.app_id
                FROM mapped m
                WHERE o.id = m.id AND (m.rn = 1 OR o.external_id IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM sales.orders x
                                  WHERE x.origin = o.origin AND x.source_app_id = m.app_id AND x.external_id = o.external_id AND x.id <> o.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_challenges",
                schema: "parties");

            migrationBuilder.DropIndex(
                name: "ix_orders_source_connection_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "email_set_by_app",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "phone_set_by_app",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "secret_version",
                schema: "shared",
                table: "outbound_webhook_subscriptions");

            migrationBuilder.DropColumn(
                name: "source_connection_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "webhook_secret_version",
                schema: "identity",
                table: "app_tokens");
        }
    }
}
