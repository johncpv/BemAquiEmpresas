using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <summary>
    /// CAT-01: o módulo <c>catalog</c> entra nos planos existentes (o "Piloto" tem todos os módulos, ADR 0010 item 4). Só acréscimo em
    /// <c>plan_modules</c>; <c>tenant_modules</c> não muda: a liberação é recalculada quando o perfil é aplicado ou o plano trocado, e a
    /// Fábrica (Konebras) não tem o catálogo no perfil, então ele continua oculto e não liberado para ela (ADR 0046). Sem mudança de modelo.
    /// </summary>
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20261117210000_CatalogModuleInPlans")]
    public partial class CatalogModuleInPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO catalog.plan_modules (id, plan_id, module_code) SELECT gen_random_uuid(), p.id, 'catalog' FROM catalog.plans p ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM catalog.plan_modules WHERE module_code = 'catalog';");
        }
    }
}
