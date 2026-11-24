using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosSupervisorApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount_limit_percent",
                schema: "pos",
                table: "stores",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_reason",
                schema: "sales",
                table: "sale_lines",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "list_unit_price",
                schema: "sales",
                table: "sale_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "price_reason",
                schema: "sales",
                table: "sale_lines",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supervisor_approvals",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supervisor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    after_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    online = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supervisor_approvals", x => x.id);
                    table.CheckConstraint("ck_supervisor_approvals_other_person", "supervisor_id <> operator_id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_discount_limit",
                schema: "pos",
                table: "stores",
                sql: "discount_limit_percent BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "ix_supervisor_approvals_approved_at_store_id",
                schema: "pos",
                table: "supervisor_approvals",
                columns: new[] { "approved_at", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supervisor_approvals_sale_id",
                schema: "pos",
                table: "supervisor_approvals",
                column: "sale_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supervisor_approvals",
                schema: "pos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_discount_limit",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "discount_limit_percent",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "discount_reason",
                schema: "sales",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "list_unit_price",
                schema: "sales",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "price_reason",
                schema: "sales",
                table: "sale_lines");
        }
    }
}
