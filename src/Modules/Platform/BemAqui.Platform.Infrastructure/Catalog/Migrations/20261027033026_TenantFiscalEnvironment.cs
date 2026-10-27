using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class TenantFiscalEnvironment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fiscal_environment_allowed",
                schema: "catalog",
                table: "tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            // FIS-01 CA 9 (ADR 0027 §6): só o demo emite, e só em homologação; o konebras fica None até a GOL-01b (ADR 0034).
            migrationBuilder.Sql("UPDATE catalog.tenants SET fiscal_environment_allowed = 'Homologation' WHERE key = 'demo';");
            migrationBuilder.Sql("ALTER TABLE catalog.tenants ADD CONSTRAINT ck_tenants_fiscal_environment_allowed CHECK (fiscal_environment_allowed IN ('None', 'Homologation', 'Production'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE catalog.tenants DROP CONSTRAINT IF EXISTS ck_tenants_fiscal_environment_allowed;");
            migrationBuilder.DropColumn(
                name: "fiscal_environment_allowed",
                schema: "catalog",
                table: "tenants");
        }
    }
}
