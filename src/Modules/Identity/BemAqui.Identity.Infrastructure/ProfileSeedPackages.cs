using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Pacote do Comércio nos acessos (PERF-04 CA 1): grupos Gerente de loja, Operador de caixa e Estoquista, só com permissões de módulos
/// que já existem (as do <c>pos</c> e do <c>sales</c> entram na PDV-01). Cada grupo nasce uma vez pela <c>seed_key</c>; renomeado,
/// inativado ou com permissões mudadas, o seed não mexe. Nome já usado por outro grupo do tenant: o grupo do seed não é criado.
/// </summary>
public sealed class IdentityRetailSeedPackage : IProfileSeedPackage
{
    public static readonly IReadOnlyList<(string Key, string Name, string Description, (string Module, string[] Actions)[] Grants)> Seeds =
    [
        ("store_manager", "Gerente de loja", "Cuida da loja: estoque, compras, clientes e o financeiro do dia a dia.",
            [("dashboard", ["view"]), ("finance", ["view", "create", "edit"]), ("inventory", ["view", "create", "edit"]), ("purchasing", ["view", "create"]), ("parties", ["view", "create", "edit"])]),
        ("cashier", "Operador de caixa", "Atende no caixa. As permissões da venda e do caixa chegam com o PDV.",
            [("parties", ["view", "create"]), ("inventory", ["view"])]),
        ("stock_clerk", "Estoquista", "Recebe mercadoria, confere e movimenta o estoque.",
            [("inventory", ["view", "create", "edit"]), ("purchasing", ["view"])]),
    ];

    public string Key => BusinessProfileSeeds.IdentityRetail;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var existing = (await db.Groups.IgnoreQueryFilters().Where(g => g.SeedKey != null).Select(g => g.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var names = (await db.Groups.Select(g => g.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name, description, grants) in Seeds.Where(s => !existing.Contains(s.Key) && !names.Contains(s.Name)))
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
