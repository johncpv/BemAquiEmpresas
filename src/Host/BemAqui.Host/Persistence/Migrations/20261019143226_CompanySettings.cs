using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "company_profile",
                schema: "settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    municipal_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city_ibge = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_formats",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prefix = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    yearly = table.Column<bool>(type: "boolean", nullable: false),
                    digits = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_formats", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_number_formats_doc_type",
                schema: "shared",
                table: "number_formats",
                column: "doc_type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_profile",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "number_formats",
                schema: "shared");
        }
    }
}
