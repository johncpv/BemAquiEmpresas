using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Platform.Infrastructure.Catalog.Migrations
{
    /// <summary>
    /// PDV-01: os módulos <c>pos</c> (caixa) e <c>sales</c> (vendas, VND-01, que não tinha entrado nos planos existentes) entram nos planos já
    /// gravados (o "Piloto" tem todos os módulos, ADR 0010 item 4), como a CAT-01 fez com o <c>catalog</c>. Só acréscimo em <c>plan_modules</c>;
    /// <c>tenant_modules</c> não muda: a liberação é recalculada quando o perfil é aplicado ou o plano trocado, e a Fábrica (Konebras) não tem esses
    /// módulos no perfil, então eles continuam ocultos e não liberados para ela (ADR 0046). Sem mudança de modelo.
    /// </summary>
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20261118120000_PosModuleInPlans")]
    public partial class PosModuleInPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO catalog.plan_modules (id, plan_id, module_code) SELECT gen_random_uuid(), p.id, m.code FROM catalog.plans p CROSS JOIN (VALUES ('sales'), ('pos')) AS m(code) ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM catalog.plan_modules WHERE module_code = 'pos';");
        }
    }
}
