using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Off03OfflineAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cash_session_id",
                schema: "pos",
                table: "supervisor_approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "offline_approval_max_amount",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 50.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "offline_approval_review_amount",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 100.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "offline_approval_session_amount",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 150.00m);

            migrationBuilder.AddColumn<int>(
                name: "offline_approval_session_count",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "offline_cancel_sale_allowed",
                schema: "pos",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "offline",
                schema: "pos",
                table: "session_sales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sale_number",
                schema: "pos",
                table: "session_sales",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offline_code_hash",
                schema: "identity",
                table: "pos_pins",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offline_code_set_at",
                schema: "identity",
                table: "pos_pins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "closed_offline",
                schema: "pos",
                table: "cash_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "opened_offline",
                schema: "pos",
                table: "cash_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "device_pin_verifiers",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pin_verifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_pin_verifiers_shopfloor_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "identity",
                        principalTable: "shopfloor_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_pin_verifiers_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supervisor_approvals_cash_session_id",
                schema: "pos",
                table: "supervisor_approvals",
                column: "cash_session_id",
                filter: "cash_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_pin_verifiers_device_id_user_id_kind",
                schema: "identity",
                table: "device_pin_verifiers",
                columns: new[] { "device_id", "user_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_pin_verifiers_user_id",
                schema: "identity",
                table: "device_pin_verifiers",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_pin_verifiers",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_supervisor_approvals_cash_session_id",
                schema: "pos",
                table: "supervisor_approvals");

            migrationBuilder.DropColumn(
                name: "cash_session_id",
                schema: "pos",
                table: "supervisor_approvals");

            migrationBuilder.DropColumn(
                name: "offline_approval_max_amount",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline_approval_review_amount",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline_approval_session_amount",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline_approval_session_count",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline_cancel_sale_allowed",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline",
                schema: "pos",
                table: "session_sales");

            migrationBuilder.DropColumn(
                name: "sale_number",
                schema: "pos",
                table: "session_sales");

            migrationBuilder.DropColumn(
                name: "offline_code_hash",
                schema: "identity",
                table: "pos_pins");

            migrationBuilder.DropColumn(
                name: "offline_code_set_at",
                schema: "identity",
                table: "pos_pins");

            migrationBuilder.DropColumn(
                name: "closed_offline",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "opened_offline",
                schema: "pos",
                table: "cash_sessions");
        }
    }
}
