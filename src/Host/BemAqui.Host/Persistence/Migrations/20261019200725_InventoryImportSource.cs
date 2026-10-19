using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryImportSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "import_source",
                schema: "inventory",
                table: "units",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "import_source",
                schema: "inventory",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "import_source",
                schema: "inventory",
                table: "item_supplier_codes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "import_source",
                schema: "inventory",
                table: "item_categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "import_source",
                schema: "inventory",
                table: "units");

            migrationBuilder.DropColumn(
                name: "import_source",
                schema: "inventory",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "import_source",
                schema: "inventory",
                table: "item_supplier_codes");

            migrationBuilder.DropColumn(
                name: "import_source",
                schema: "inventory",
                table: "item_categories");
        }
    }
}
