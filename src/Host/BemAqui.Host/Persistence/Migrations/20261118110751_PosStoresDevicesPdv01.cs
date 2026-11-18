using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosStoresDevicesPdv01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "identity",
                table: "shopfloor_devices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Factory");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sync_at",
                schema: "identity",
                table: "shopfloor_devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_key",
                schema: "identity",
                table: "shopfloor_devices",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "register_epoch",
                schema: "identity",
                table: "shopfloor_devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "register_id",
                schema: "identity",
                table: "shopfloor_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                schema: "identity",
                table: "shopfloor_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    stock_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    seed_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registers",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    cash_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ui_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    active_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_epoch = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registers", x => x.id);
                    table.CheckConstraint("ck_registers_epoch", "device_epoch >= 0");
                    table.CheckConstraint("ck_registers_number", "number BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "fk_registers_stores_store_id",
                        column: x => x.store_id,
                        principalSchema: "pos",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shopfloor_devices_register_id",
                schema: "identity",
                table: "shopfloor_devices",
                column: "register_id",
                filter: "register_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registers_active_device_id",
                schema: "pos",
                table: "registers",
                column: "active_device_id",
                unique: true,
                filter: "active_device_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registers_cash_account",
                schema: "pos",
                table: "registers",
                column: "cash_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registers_store_number",
                schema: "pos",
                table: "registers",
                columns: new[] { "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stores_code",
                schema: "pos",
                table: "stores",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stores_seed_key",
                schema: "pos",
                table: "stores",
                column: "seed_key",
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registers",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "ix_shopfloor_devices_register_id",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "last_sync_at",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "public_key",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "register_epoch",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "register_id",
                schema: "identity",
                table: "shopfloor_devices");

            migrationBuilder.DropColumn(
                name: "store_id",
                schema: "identity",
                table: "shopfloor_devices");
        }
    }
}
