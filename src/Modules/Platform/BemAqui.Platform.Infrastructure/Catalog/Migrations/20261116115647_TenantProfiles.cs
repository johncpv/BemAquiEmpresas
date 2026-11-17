using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class TenantProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_profiles",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    segment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_by = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_profiles_active",
                schema: "catalog",
                table: "tenant_profiles",
                columns: new[] { "tenant_id", "profile_code", "segment" },
                unique: true,
                filter: "removed_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            // PERF-01 (ADR 0046): aplicar ou remover perfil invalida o cache do tenant; todo tenant existente (Konebras e demo)
            // recebe o perfil Fábrica, que lista os 16 módulos de hoje, então tenant_modules não muda (CA 4 e CA 11).
            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.InstallProfiles);
            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.BackfillFactoryProfile);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BemAqui.Platform.Infrastructure.Catalog.CatalogNotifySql.UninstallProfiles);

            migrationBuilder.DropTable(
                name: "tenant_profiles",
                schema: "catalog");
        }
    }
}
