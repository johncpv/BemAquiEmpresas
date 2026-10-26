using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>Um seed patch: chave estável e as concessões por grupo do seed (pela <c>seed_key</c>).</summary>
public sealed record GroupGrantPatch(string Key, IReadOnlyList<(string SeedKey, string Module, string[] Actions)> Grants);

/// <summary>
/// Seed patches (ADR 0016, CAD-01a CA 22): roda depois do <see cref="GroupSeeder"/>, em todo provisionamento e todo
/// <c>migrate</c>. Cada patch roda uma vez por tenant: só <b>acrescenta</b> módulo e ações a grupos com <c>seed_key</c>
/// (nunca remove, nunca toca grupo criado pelo tenant) e invalida o cache de permissão dos membros.
/// Aplicado pelo sistema, fora da delegação da ADR 0008.
/// </summary>
public sealed class SeedPatchSeeder : ITenantSeeder
{
    /// <summary>Patches em ordem. Patch novo entra no fim, com chave nova; nunca altere um já publicado.</summary>
    public static readonly IReadOnlyList<GroupGrantPatch> Patches =
    [
        new("parties-default-permissions",
        [
            ("commercial", "parties", ["view", "create", "edit"]),
            ("finance", "parties", ["view", "create", "edit"]),
            ("warehouse", "parties", ["view", "create", "edit"]),
            ("engineering", "parties", ["view"]),
        ]),
        // ADR 0008 adendo D5: o Financeiro paga a OP aprovada; aprovar continua só com o Proprietário (FIN-03a CA 1).
        new("finance-pay-financeiro",
        [
            ("finance", "finance", ["pay"]),
        ]),
        // D7: custo e margem são sigilo comercial; Comercial e Financeiro veem (o Proprietário tem tudo). COM-01a CA 1.
        new("view-cost-default-permissions",
        [
            ("commercial", "quotes", ["view_cost"]),
            ("finance", "quotes", ["view_cost"]),
            ("commercial", "projects", ["view_cost"]),
            ("finance", "projects", ["view_cost"]),
        ]),
        // COM-06 CA 1: a Engenharia cria a OS de instalação a partir do projeto.
        new("installation-orders-permissions",
        [
            ("engineering", "production", ["create"]),
        ]),
        // ENG-01 CA 1 (D13, ADR 0003 adendo de 21/10/2026): a Engenharia cadastra os roteiros e a Supervisão consulta.
        // engineering.approve (liberar a BOM) é da ENG-02.
        new("engineering-permissions",
        [
            ("engineering", "engineering", ["view", "create", "edit", "delete"]),
            ("production_supervision", "engineering", ["view"]),
        ]),
        // PRD-05 CA 1 (D6, ADR 0003 adendo de 21/10/2026): a Supervisão decidia a qualidade por production.approve e continua
        // decidindo pela ação nova; valor inicial do seed, o tenant ajusta.
        new("production-quality",
        [
            ("production_supervision", "production", ["approve_quality"]),
        ]),
        // CAD-04c1b (PO 22/10/2026, ADR 0003 adendo de 21/10/2026): o RH do seed via o tipo de afastamento pela hr.view_sensitive
        // e continua vendo pela ação nova de dado de saúde; o Proprietário tem tudo. A RH-01b usa a mesma ação.
        new("hr-view-health",
        [
            ("hr", "hr", ["view_health"]),
        ]),
        // ENG-02 CA 1 (D13, ADR 0008 adendo de 21/10/2026): a Engenharia libera a BOM. Aplicado pelo sistema, fora da delegação; fora do
        // seed, só um membro do Proprietário concede engineering.approve, e quem a tem passa a ter 2FA obrigatório no login.
        new("engineering-release-bom",
        [
            ("engineering", "engineering", ["approve"]),
        ]),
        // CMP-01 CA 2 (D8, ADR 0024 §5): o Almoxarifado vê as compras e registra o recebimento (CMP-04), sem pedir, editar nem aprovar; o
        // Financeiro acompanha. purchasing.create, edit e approve ficam só com o Proprietário (o tenant concede a quem compra).
        new("purchasing-permissions",
        [
            ("warehouse", "purchasing", ["view", "receive"]),
            ("finance", "purchasing", ["view"]),
        ]),
        // EST-01b CA 1 (PO 21/10/2026, revisão pré-Fase 3 item 17): o Almoxarifado lança o ajuste de estoque; aprovar continua só com o
        // Proprietário (inventory.approve é reservada, ADR 0008).
        new("inventory-adjust-warehouse",
        [
            ("warehouse", "inventory", ["adjust"]),
        ]),
        // PNL-01a CA 1 (D11, ADR 0003 adendo de 21/10/2026, ADR 0030 §4): a Supervisão de produção, que já cria as telas, passa a revogá-las.
        new("kiosk-delete",
        [
            ("production_supervision", "kiosk", ["delete"]),
        ]),
    ];

    public int Order => 25;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var applied = (await db.SeedPatches.Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var patch in Patches.Where(p => !applied.Contains(p.Key)))
        {
            var touched = new HashSet<Guid>();
            foreach (var (seedKey, module, actions) in patch.Grants)
            {
                var group = await db.Groups.FirstOrDefaultAsync(g => g.SeedKey == seedKey && !g.IsSystem, cancellationToken);
                if (group is null)
                {
                    continue;
                }

                if (!await db.GroupModules.AnyAsync(m => m.GroupId == group.Id && m.ModuleCode == module, cancellationToken))
                {
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                }

                var existing = await db.GroupPermissions.Where(p => p.GroupId == group.Id && p.ModuleCode == module).Select(p => p.Action).ToListAsync(cancellationToken);
                db.GroupPermissions.AddRange(actions.Except(existing).Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = a }));
                touched.Add(group.Id);
            }

            db.SeedPatches.Add(new SeedPatch { Key = patch.Key, AppliedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            var members = await db.UserGroups.Where(ug => touched.Contains(ug.GroupId)).Select(ug => ug.UserId).Distinct().ToListAsync(cancellationToken);
            await tenantScope.GetRequiredService<IPermissionService>().BumpAsync(members, cancellationToken);
        }
    }
}
