using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosCatalogReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "catalog_change_seq",
                schema: "pos");

            migrationBuilder.AddColumn<decimal>(
                name: "cash_break_tolerance",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 5.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_drop_alert_amount",
                schema: "pos",
                table: "stores",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1000.00m);

            migrationBuilder.AddColumn<int>(
                name: "exchange_days",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "offline_window_hours",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 168);

            migrationBuilder.CreateTable(
                name: "catalog_items",
                schema: "pos",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    price_list_version = table.Column<long>(type: "bigint", nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('pos.catalog_change_seq')"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_items", x => new { x.store_id, x.kind, x.entity_id });
                });

            migrationBuilder.CreateTable(
                name: "catalog_store_builds",
                schema: "pos",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_store_builds", x => x.store_id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_offline_window",
                schema: "pos",
                table: "stores",
                sql: "offline_window_hours BETWEEN 1 AND 168");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_parameters",
                schema: "pos",
                table: "stores",
                sql: "cash_break_tolerance >= 0 AND exchange_days >= 0 AND cash_drop_alert_amount >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_items_store_change_seq",
                schema: "pos",
                table: "catalog_items",
                columns: new[] { "store_id", "change_seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_items",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "catalog_store_builds",
                schema: "pos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_offline_window",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_parameters",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "cash_break_tolerance",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "cash_drop_alert_amount",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "exchange_days",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "offline_window_hours",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropSequence(
                name: "catalog_change_seq",
                schema: "pos");
        }
    }
}
