using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CnpjLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cnpj_lookups",
                schema: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    looked_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cnpj_lookups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cnpj_lookups_document",
                schema: "parties",
                table: "cnpj_lookups",
                column: "document",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cnpj_lookups_looked_up_at",
                schema: "parties",
                table: "cnpj_lookups",
                column: "looked_up_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cnpj_lookups",
                schema: "parties");
        }
    }
}
