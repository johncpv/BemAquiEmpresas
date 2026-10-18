using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Deixa <c>identity.modules</c> e <c>identity.module_actions</c> iguais ao <c>ModuleCatalog</c> (FND-06 CA 1). Roda no
/// provisionamento e a cada deploy; é idempotente e só mexe nessas duas tabelas (grupos e permissões ficam intactos).
/// </summary>
public sealed class ModuleCatalogSeeder : ITenantSeeder
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();

        var modules = await db.CatalogModules.ToDictionaryAsync(m => m.Code, StringComparer.Ordinal, cancellationToken);
        foreach (var definition in ModuleCatalog.All)
        {
            if (!modules.TryGetValue(definition.Code, out var module))
            {
                module = new CatalogModule { Code = definition.Code };
                db.CatalogModules.Add(module);
            }

            module.Name = definition.Name;
            module.Area = definition.Area;
            module.Phase = definition.Phase;
            module.Order = definition.Order;
            module.Description = definition.Description;
            module.ApproveMeaning = definition.ApproveMeaning;
        }

        db.CatalogModules.RemoveRange(modules.Values.Where(m => ModuleCatalog.Find(m.Code) is null));

        var wanted = ModuleCatalog.All.SelectMany(m =>
            m.Standard.Select(a => (Module: m.Code, Action: a, Label: StandardActions.Label(a), Description: string.Empty, Standard: true))
                .Concat(m.Specific.Select(s => (Module: m.Code, Action: s.Code, s.Label, s.Description, Standard: false))))
            .ToList();
        var actions = await db.CatalogModuleActions.ToListAsync(cancellationToken);
        foreach (var (moduleCode, action, label, description, standard) in wanted)
        {
            var row = actions.FirstOrDefault(a => a.ModuleCode == moduleCode && a.Action == action);
            if (row is null)
            {
                row = new CatalogModuleAction { ModuleCode = moduleCode, Action = action };
                db.CatalogModuleActions.Add(row);
            }

            row.Label = label;
            row.Description = description;
            row.IsStandard = standard;
        }

        db.CatalogModuleActions.RemoveRange(actions.Where(a => !wanted.Any(w => w.Module == a.ModuleCode && w.Action == a.Action)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
