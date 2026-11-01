using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollTaxTablesFlh01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_tax_tables",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rectifies_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rectify_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    rounding = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    seed_package = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_by = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    checked_with_dataset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_tax_tables", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_tax_tables_payroll_tax_tables_rectifies_id",
                        column: x => x.rectifies_id,
                        principalSchema: "hr",
                        principalTable: "payroll_tax_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_tax_table_datasets",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cases = table.Column<string>(type: "jsonb", nullable: false),
                    case_count = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_tax_table_datasets", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_tax_table_datasets_payroll_tax_tables_table_id",
                        column: x => x.table_id,
                        principalSchema: "hr",
                        principalTable: "payroll_tax_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_tax_table_datasets_table_id",
                schema: "hr",
                table: "payroll_tax_table_datasets",
                column: "table_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_tax_tables_current",
                schema: "hr",
                table: "payroll_tax_tables",
                columns: new[] { "kind", "valid_from" },
                unique: true,
                filter: "status = 'Active' OR (status = 'Unconfirmed' AND rectifies_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_tax_tables_pending_rectify",
                schema: "hr",
                table: "payroll_tax_tables",
                column: "rectifies_id",
                unique: true,
                filter: "status = 'Unconfirmed' AND rectifies_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_tax_tables_seed",
                schema: "hr",
                table: "payroll_tax_tables",
                columns: new[] { "seed_package", "kind" },
                unique: true,
                filter: "seed_package IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_tax_table_datasets",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_tax_tables",
                schema: "hr");
        }
    }
}
