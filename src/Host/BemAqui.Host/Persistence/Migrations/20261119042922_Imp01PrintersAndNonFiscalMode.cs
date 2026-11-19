using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Imp01PrintersAndNonFiscalMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fiscal_mode",
                schema: "pos",
                table: "stores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Nfce");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "non_fiscal_accepted_at",
                schema: "pos",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "non_fiscal_accepted_by",
                schema: "pos",
                table: "stores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "non_fiscal_terms_version",
                schema: "pos",
                table: "stores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_print_mode",
                schema: "pos",
                table: "stores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Ask");

            migrationBuilder.CreateTable(
                name: "print_jobs",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    copy = table.Column<int>(type: "integer", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_print_jobs", x => x.id);
                    table.CheckConstraint("ck_print_jobs_copy", "copy >= 1");
                    table.ForeignKey(
                        name: "fk_print_jobs_registers_register_id",
                        column: x => x.register_id,
                        principalSchema: "pos",
                        principalTable: "registers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "printers",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    connection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paper_width_mm = table.Column<int>(type: "integer", nullable: false),
                    columns = table.Column<int>(type: "integer", nullable: false),
                    model = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    test_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printers", x => x.id);
                    table.CheckConstraint("ck_printers_layout", "(paper_width_mm = 58 AND columns = 32) OR (paper_width_mm = 80 AND columns IN (42, 48))");
                    table.ForeignKey(
                        name: "fk_printers_registers_register_id",
                        column: x => x.register_id,
                        principalSchema: "pos",
                        principalTable: "registers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_printers_stores_store_id",
                        column: x => x.store_id,
                        principalSchema: "pos",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_print_jobs_register_id",
                schema: "pos",
                table: "print_jobs",
                column: "register_id");

            migrationBuilder.CreateIndex(
                name: "ix_print_jobs_sale_id",
                schema: "pos",
                table: "print_jobs",
                column: "sale_id",
                filter: "sale_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_print_jobs_store_id_printed_at",
                schema: "pos",
                table: "print_jobs",
                columns: new[] { "store_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_printers_register_default",
                schema: "pos",
                table: "printers",
                column: "register_id",
                unique: true,
                filter: "is_default AND is_active");

            migrationBuilder.CreateIndex(
                name: "ix_printers_store_id",
                schema: "pos",
                table: "printers",
                column: "store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "print_jobs",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "printers",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "fiscal_mode",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "non_fiscal_accepted_at",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "non_fiscal_accepted_by",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "non_fiscal_terms_version",
                schema: "pos",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "receipt_print_mode",
                schema: "pos",
                table: "stores");
        }
    }
}
