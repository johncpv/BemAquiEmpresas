using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Uma entidade auditável e o módulo dono (para o histórico exigir <c>{modulo}.view</c>).</summary>
public sealed record AuditableEntity(string Name, string Module);

/// <summary>
/// Qual módulo é dono de cada entidade auditável (para o histórico exigir a permissão <c>view</c> do dono).
/// Cada módulo registra as suas no <c>AddServices</c> com <see cref="AuditableEntityServiceCollectionExtensions.AddAuditableEntity{TEntity}"/>;
/// entidade não registrada não tem histórico consultável.
/// </summary>
public sealed class AuditableEntityRegistry
{
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    public AuditableEntityRegistry()
    {
    }

    public AuditableEntityRegistry(IEnumerable<AuditableEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (var entity in entities)
        {
            _owners[entity.Name] = entity.Module;
        }
    }

    public AuditableEntityRegistry Register<TEntity>(string moduleCode)
    {
        _owners[typeof(TEntity).Name] = moduleCode;
        return this;
    }

    public string? OwnerOf(string entity) => _owners.GetValueOrDefault(entity);
}

public static class AuditableEntityServiceCollectionExtensions
{
    public static IServiceCollection AddAuditableEntity<TEntity>(this IServiceCollection services, string moduleCode)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(new AuditableEntity(typeof(TEntity).Name, moduleCode));
    }
}
