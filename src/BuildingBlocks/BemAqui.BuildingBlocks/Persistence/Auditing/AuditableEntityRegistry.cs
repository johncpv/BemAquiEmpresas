using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Uma entidade auditável e o módulo dono (para o histórico exigir <c>{modulo}.view</c>). <c>Name</c> é o nome curto
/// gravado em <c>audit_log.entity</c>; <c>QualifiedName</c> identifica a classe para detectar colisão (CAD-01a CA 26).
/// </summary>
/// <param name="ReadAction">Ação exigida para ler o histórico: <c>view</c>, ou <c>view_sensitive</c> no HR (CAD-04a CA 13).</param>
/// <param name="RestrictedFields">
/// Campos do diff (camelCase) que só aparecem no histórico para quem tem a ação dada do módulo dono; sem ela, o campo sai da
/// entrada (ex.: o valor de aquisição do bem só com <c>assets.edit</c>, PAT-01a CA 16).
/// </param>
public sealed record AuditableEntity(string Name, string Module, string? QualifiedName = null, string ReadAction = "view", IReadOnlyDictionary<string, string>? RestrictedFields = null);

/// <summary>
/// Qual módulo é dono de cada entidade auditável (para o histórico exigir a permissão <c>view</c> do dono).
/// Cada módulo registra as suas no <c>AddServices</c> com <see cref="AuditableEntityServiceCollectionExtensions.AddAuditableEntity{TEntity}"/>;
/// entidade não registrada não tem histórico consultável.
/// </summary>
public sealed class AuditableEntityRegistry
{
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _readActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _restricted = new(StringComparer.Ordinal);

    public AuditableEntityRegistry()
    {
    }

    public AuditableEntityRegistry(IEnumerable<AuditableEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            // Duas classes com o mesmo nome curto em módulos diferentes (ex.: Address no HR e no Parties) fariam o
            // histórico de uma valer com a permissão da outra: a subida falha em vez de escolher uma.
            var qualified = entity.QualifiedName ?? entity.Name;
            if (types.TryGetValue(entity.Name, out var other) && other != qualified)
            {
                throw new InvalidOperationException($"Entidade auditável com nome repetido: {other} e {qualified} usam \"{entity.Name}\". Renomeie uma delas (ex.: PartyAddress, EmployeeAddress).");
            }

            if (_owners.TryGetValue(entity.Name, out var module) && module != entity.Module)
            {
                throw new InvalidOperationException($"Entidade auditável {entity.Name} registrada por dois módulos ({module} e {entity.Module}).");
            }

            types[entity.Name] = qualified;
            _owners[entity.Name] = entity.Module;
            _readActions[entity.Name] = entity.ReadAction;
            if (entity.RestrictedFields is { Count: > 0 } restricted)
            {
                _restricted[entity.Name] = restricted;
            }
        }
    }

    public AuditableEntityRegistry Register<TEntity>(string moduleCode)
    {
        _owners[typeof(TEntity).Name] = moduleCode;
        return this;
    }

    public string? OwnerOf(string entity) => _owners.GetValueOrDefault(entity);

    public string ReadActionOf(string entity) => _readActions.GetValueOrDefault(entity, "view");

    /// <summary>Campos do diff restritos a uma ação do módulo dono (campo → ação), ou vazio.</summary>
    public IReadOnlyDictionary<string, string> RestrictedFieldsOf(string entity) =>
        _restricted.GetValueOrDefault(entity) ?? new Dictionary<string, string>();
}

public static class AuditableEntityServiceCollectionExtensions
{
    public static IServiceCollection AddAuditableEntity<TEntity>(this IServiceCollection services, string moduleCode, string readAction = "view")
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(new AuditableEntity(typeof(TEntity).Name, moduleCode, typeof(TEntity).FullName, readAction));
    }

    /// <summary>Como a outra, com campos do diff que só aparecem para quem tem a ação dada (campo em camelCase → ação do módulo).</summary>
    public static IServiceCollection AddAuditableEntity<TEntity>(this IServiceCollection services, string moduleCode, string readAction, IReadOnlyDictionary<string, string> restrictedFields)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(new AuditableEntity(typeof(TEntity).Name, moduleCode, typeof(TEntity).FullName, readAction, restrictedFields));
    }
}
