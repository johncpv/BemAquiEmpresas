using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class TenantWebhookKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_key",
                schema: "catalog",
                table: "tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_webhook_key",
                schema: "catalog",
                table: "tenants",
                column: "webhook_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenants_webhook_key",
                schema: "catalog",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "webhook_key",
                schema: "catalog",
                table: "tenants");
        }
    }
}
