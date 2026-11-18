using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_runs",
                schema: "catalog_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    to_create = table.Column<int>(type: "integer", nullable: false),
                    to_update = table.Column<int>(type: "integer", nullable: false),
                    error_rows = table.Column<int>(type: "integer", nullable: false),
                    stock_rows = table.Column<int>(type: "integer", nullable: false),
                    prices_ignored = table.Column<bool>(type: "boolean", nullable: false),
                    rows = table.Column<string>(type: "jsonb", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    apply_prices = table.Column<bool>(type: "boolean", nullable: false),
                    launch_stock = table.Column<bool>(type: "boolean", nullable: false),
                    stock_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_background = table.Column<bool>(type: "boolean", nullable: false),
                    processed = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    stock_posted = table.Column<int>(type: "integer", nullable: false),
                    stock_already_posted = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_import_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_import_runs_created_at",
                schema: "catalog_products",
                table: "import_runs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_runs",
                schema: "catalog_products");
        }
    }
}
