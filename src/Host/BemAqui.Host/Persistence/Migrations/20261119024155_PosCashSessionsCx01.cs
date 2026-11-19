using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosCashSessionsCx01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cash_ceiling_amount",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pos_cash_movement_id",
                schema: "finance",
                table: "bank_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_epoch = table.Column<int>(type: "integer", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_sessions", x => x.id);
                    table.CheckConstraint("ck_cash_sessions_opening", "opening_amount >= 0");
                    table.ForeignKey(
                        name: "fk_cash_sessions_registers_register_id",
                        column: x => x.register_id,
                        principalSchema: "pos",
                        principalTable: "registers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    above_expected = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                    table.CheckConstraint("ck_cash_movements_amount", "amount >= 0 AND (kind = 'Opening' OR amount > 0)");
                    table.CheckConstraint("ck_cash_movements_reason", "kind = 'Opening' OR reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_cash_movements_cash_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "pos",
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_cash_ceiling",
                schema: "pos",
                table: "stores",
                sql: "cash_ceiling_amount IS NULL OR cash_ceiling_amount > 0");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_pos_cash_movement_id",
                schema: "finance",
                table: "bank_transfers",
                column: "pos_cash_movement_id",
                unique: true,
                filter: "pos_cash_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_session_id",
                schema: "pos",
                table: "cash_movements",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_register_open",
                schema: "pos",
                table: "cash_sessions",
                column: "register_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_store_id_opened_at",
                schema: "pos",
                table: "cash_sessions",
                columns: new[] { "store_id", "opened_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "cash_sessions",
                schema: "pos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_cash_ceiling",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropIndex(
                name: "ix_bank_transfers_pos_cash_movement_id",
                schema: "finance",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "cash_ceiling_amount",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "pos_cash_movement_id",
                schema: "finance",
                table: "bank_transfers");
        }
    }
}
