using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class TenantModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plan_modules",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_modules_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "catalog",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_modules",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_exception = table.Column<bool>(type: "boolean", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_modules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_modules_plan_id_module_code",
                schema: "catalog",
                table: "plan_modules",
                columns: new[] { "plan_id", "module_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_modules_tenant_id_module_code",
                schema: "catalog",
                table: "tenant_modules",
                columns: new[] { "tenant_id", "module_code" },
                unique: true);

            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.InstallModules);

            // Planos e tenants que já existiam: plano completo (ADR 0010 item 4) e tenant herdando do plano.
            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.BackfillModules);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.UninstallModules);

            migrationBuilder.DropTable(
                name: "plan_modules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tenant_modules",
                schema: "catalog");
        }
    }
}
