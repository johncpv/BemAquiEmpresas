using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosCashClosingCx02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_reason",
                schema: "pos",
                table: "cash_sessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "pos",
                table: "cash_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "count_id",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "count_started_at",
                schema: "pos",
                table: "cash_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "counted_at",
                schema: "pos",
                table: "cash_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "counted_by",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                schema: "pos",
                table: "cash_sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deposit_movement_id",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "device_sales_count",
                schema: "pos",
                table: "cash_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "kept_amount",
                schema: "pos",
                table: "cash_sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reopen_count",
                schema: "pos",
                table: "cash_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "reopen_reason",
                schema: "pos",
                table: "cash_sessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reopened_at",
                schema: "pos",
                table: "cash_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reopened_by",
                schema: "pos",
                table: "cash_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "review_reasons",
                schema: "pos",
                table: "cash_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "cash_counts",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    counted = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    denominations = table.Column<string>(type: "jsonb", nullable: true),
                    expected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pix_not_found = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_counts", x => x.id);
                    table.CheckConstraint("ck_cash_counts_counted", "counted >= 0");
                    table.ForeignKey(
                        name: "fk_cash_counts_cash_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "pos",
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_pix_checks",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    checked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_pix_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_cash_pix_checks_cash_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "pos",
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_register_id_opened_at",
                schema: "pos",
                table: "cash_sessions",
                columns: new[] { "register_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_counts_session_id_count_id",
                schema: "pos",
                table: "cash_counts",
                columns: new[] { "session_id", "count_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_pix_checks_session_id_payment_id",
                schema: "pos",
                table: "cash_pix_checks",
                columns: new[] { "session_id", "payment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_counts",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "cash_pix_checks",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "ix_cash_sessions_register_id_opened_at",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "approval_reason",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "closed_by",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "count_id",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "count_started_at",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "counted_at",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "counted_by",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "deposit_movement_id",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "device_sales_count",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "kept_amount",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "reopen_count",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "reopen_reason",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "reopened_by",
                schema: "pos",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "review_reasons",
                schema: "pos",
                table: "cash_sessions");
        }
    }
}
