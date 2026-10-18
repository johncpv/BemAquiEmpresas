using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AuditOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "audit",
                table: "audit_log",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origin",
                schema: "audit",
                table: "audit_log");
        }
    }
}
