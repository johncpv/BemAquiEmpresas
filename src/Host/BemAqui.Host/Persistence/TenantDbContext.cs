using BemAqui.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Persistence;

/// <summary>
/// DbContext único do banco de tenant (ADR 0006): tabelas compartilhadas da base e, conforme as stories
/// avançam, as configurações de cada módulo. A conexão por tenant chega na FND-02.
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : BemAquiDbContext(options)
{
    protected override void ConfigureModules(ModelBuilder modelBuilder)
    {
        foreach (var module in ModuleRegistry.All)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(module.GetType().Assembly);
        }
    }
}
