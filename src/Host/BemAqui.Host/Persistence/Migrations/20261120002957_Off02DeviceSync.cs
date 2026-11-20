using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Off02DeviceSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "free_remove_last",
                schema: "pos",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "sync_alert_hours",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.CreateTable(
                name: "blind_index_keys",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_cipher = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blind_index_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_events",
                schema: "pos",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_epoch = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_skew_ms = table.Column<long>(type: "bigint", nullable: false),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chain_ok = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    device_revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "operator_incidents",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    authorized_by = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_snapshots",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perm_version = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_owner = table.Column<bool>(type: "boolean", nullable: false),
                    permissions = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_snapshots", x => new { x.user_id, x.perm_version });
                    table.ForeignKey(
                        name: "fk_permission_snapshots_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_customer_documents",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    document_cipher = table.Column<string>(type: "text", nullable: false),
                    blind_index = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_customer_documents", x => x.id);
                    table.CheckConstraint("ck_sale_customer_documents_kind", "kind IN ('cpf', 'cnpj')");
                });

            migrationBuilder.CreateTable(
                name: "session_sales",
                schema: "pos",
                columns: table => new
                {
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_sales", x => x.sale_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_divergences",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quarantined = table.Column<bool>(type: "boolean", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_epoch = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolution = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_divergences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_events_register_epoch_seq",
                schema: "pos",
                table: "device_events",
                columns: new[] { "register_id", "device_epoch", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_device_events_register_id_received_at",
                schema: "pos",
                table: "device_events",
                columns: new[] { "register_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_operator_incidents_event_id",
                schema: "pos",
                table: "operator_incidents",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_incidents_occurred_at_store_id",
                schema: "pos",
                table: "operator_incidents",
                columns: new[] { "occurred_at", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_permission_snapshots_user_id_valid_from",
                schema: "identity",
                table: "permission_snapshots",
                columns: new[] { "user_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_customer_documents_blind_index",
                schema: "sales",
                table: "sale_customer_documents",
                column: "blind_index");

            migrationBuilder.CreateIndex(
                name: "ix_sale_customer_documents_sale_id",
                schema: "sales",
                table: "sale_customer_documents",
                column: "sale_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_sales_session_id",
                schema: "pos",
                table: "session_sales",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_divergences_event_id",
                schema: "pos",
                table: "sync_divergences",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_divergences_status_store_id_kind",
                schema: "pos",
                table: "sync_divergences",
                columns: new[] { "status", "store_id", "kind" });

            // OFF-02 CA 3: o registro de eventos do caixa é imutável (só inserção). UPDATE e DELETE falham; liberar da quarentena fica na pendência.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pos.device_events_immutable() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'pos.device_events é somente inserção (% recusado)', TG_OP USING ERRCODE = 'insufficient_privilege';
                END;
                $fn$;
                CREATE TRIGGER trg_device_events_immutable BEFORE UPDATE OR DELETE ON pos.device_events
                    FOR EACH ROW EXECUTE FUNCTION pos.device_events_immutable();
                CREATE TRIGGER trg_device_events_no_truncate BEFORE TRUNCATE ON pos.device_events
                    FOR EACH STATEMENT EXECUTE FUNCTION pos.device_events_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_device_events_no_truncate ON pos.device_events; DROP TRIGGER IF EXISTS trg_device_events_immutable ON pos.device_events; DROP FUNCTION IF EXISTS pos.device_events_immutable();");

            migrationBuilder.DropTable(
                name: "blind_index_keys",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "device_events",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "operator_incidents",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "permission_snapshots",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sale_customer_documents",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "session_sales",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "sync_divergences",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "free_remove_last",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "sync_alert_hours",
                schema: "pos",
                table: "stores");
        }
    }
}
