using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Os 8 grupos iniciais do R-SEC-9 (FND-06b CA 7), com as permissões padrão da ADR 0010 item 3: o Financeiro não
/// aprova (o Proprietário aprova tudo) e o RH vê dados sensíveis. Cada grupo nasce uma vez (pela <c>seed_key</c>);
/// se o tenant renomear ou inativar, o seed não recria.
/// </summary>
public sealed class GroupSeeder : ITenantSeeder
{
    private static readonly (string Key, string Name, string Description, (string Module, string[] Actions)[] Grants)[] Seeds =
    [
        ("finance", "Financeiro", "Caixa, contas a pagar e a receber.", [("dashboard", ["view"]), ("finance", ["view", "create", "edit"]), ("projects", ["view"]), ("quotes", ["view"]), ("inventory", ["view"])]),
        ("commercial", "Comercial", "Orçamentos e acompanhamento de projetos.", [("quotes", ["view", "create", "edit"]), ("projects", ["view"])]),
        ("engineering", "Engenharia", "Projetos, estrutura de produto e roteiros.", [("projects", ["view", "edit"]), ("production", ["view", "edit"]), ("inventory", ["view"])]),
        ("production_supervision", "Supervisão de produção", "Ordens de serviço, qualidade e painéis da fábrica.",
            [("production", ["view", "create", "edit", "approve"]), ("inventory", ["view", "create"]), ("projects", ["view"]), ("kiosk", ["view", "create"]), ("shopfloor", ["view", "create"])]),
        ("operators", "Operadores", "Apontamento das etapas no app do operador.", [("shopfloor", ["view", "create"])]),
        ("warehouse", "Almoxarifado", "Estoque, recebimento e separação.", [("inventory", ["view", "create", "edit"]), ("production", ["view"])]),
        ("hr", "RH", "Funcionários, ponto e folha, com dados sensíveis.", [("hr", ["view", "create", "edit", "view_sensitive"])]),
    ];

    public int Order => 20;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var existing = (await db.Groups.Where(g => g.SeedKey != null).Select(g => g.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        if (!existing.Contains(Group.OwnerSeedKey))
        {
            db.Groups.Add(new Group
            {
                Name = "Proprietário",
                Description = "Acesso total ao que está liberado. Grupo do sistema.",
                IsSystem = true,
                RequiresTwoFactor = true,
                SeedKey = Group.OwnerSeedKey,
                CreatedAt = clock.GetUtcNow(),
            });
        }

        foreach (var (key, name, description, grants) in Seeds.Where(s => !existing.Contains(s.Key)))
        {
            var group = new Group { Name = name, Description = description, SeedKey = key, CreatedAt = clock.GetUtcNow() };
            db.Groups.Add(group);
            foreach (var (module, actions) in grants)
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                db.GroupPermissions.AddRange(actions.Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = a }));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
