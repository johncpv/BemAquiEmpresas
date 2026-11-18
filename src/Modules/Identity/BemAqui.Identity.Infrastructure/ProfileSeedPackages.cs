using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Pacote do Comércio nos acessos (PERF-04 CA 1): grupos Gerente de loja, Operador de caixa e Estoquista, só com permissões de módulos
/// que já existem. Cada grupo nasce uma vez pela <c>seed_key</c>; renomeado, inativado ou com permissões mudadas, o seed não mexe. Nome já usado
/// por outro grupo do tenant: o grupo do seed não é criado. PDV-01 (PRD do PDV §6.3): o Gerente de loja ganha as vendas, o catálogo e o caixa
/// (inclusive registrar aparelhos); o Operador de caixa vende; o Fiscal de caixa (novo) faz as ações restritas do balcão. Os grupos criados
/// antes da PDV-01 recebem as mesmas ações pelo seed patch <c>pos-retail-groups</c>.
/// </summary>
public sealed class IdentityRetailSeedPackage : IProfileSeedPackage
{
    /// <summary>Ações do caixa do Gerente de loja e do Fiscal de caixa (o Fiscal não registra aparelho, não muda preço nem vende sem NFC-e).</summary>
    public static readonly string[] ManagerPos = ["view", "create", "discount", "change_price", "cancel_item", "cancel_sale", "return", "cash_out", "cash_in", "open_drawer", "manage_devices"];

    public static readonly string[] SupervisorPos = ["view", "create", "discount", "cancel_item", "cancel_sale", "return", "cash_out", "cash_in", "open_drawer"];

    public static readonly IReadOnlyList<(string Key, string Name, string Description, (string Module, string[] Actions)[] Grants)> Seeds =
    [
        ("store_manager", "Gerente de loja", "Cuida da loja: vendas, caixas e aparelhos, estoque, compras, clientes e o financeiro do dia a dia.",
            [("dashboard", ["view"]), ("finance", ["view", "create", "edit"]), ("inventory", ["view", "create", "edit"]), ("purchasing", ["view", "create"]), ("parties", ["view", "create", "edit"]),
                ("sales", ["view", "create", "edit", "approve"]), ("catalog", ["view", "create", "edit", "edit_price"]), ("pos", ManagerPos)]),
        ("cashier", "Operador de caixa", "Atende no caixa: vende e cadastra o cliente. Ações restritas pedem o supervisor.",
            [("parties", ["view", "create"]), ("inventory", ["view"]), ("pos", ["view", "create"])]),
        ("stock_clerk", "Estoquista", "Recebe mercadoria, confere e movimenta o estoque.",
            [("inventory", ["view", "create", "edit"]), ("purchasing", ["view"])]),
        ("cash_supervisor", "Fiscal de caixa", "Supervisiona os caixas: cancela, dá desconto acima do limite, faz sangria e suprimento.",
            [("parties", ["view", "create"]), ("inventory", ["view"]), ("pos", SupervisorPos)]),
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
