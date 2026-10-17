namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Qual módulo é dono de cada entidade auditável (para o histórico exigir a permissão <c>view</c> do dono).
/// Cada módulo registra as suas no <c>AddServices</c>; entidade não registrada não tem histórico consultável.
/// </summary>
public sealed class AuditableEntityRegistry
{
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    public AuditableEntityRegistry Register<TEntity>(string moduleCode)
    {
        _owners[typeof(TEntity).Name] = moduleCode;
        return this;
    }

    public string? OwnerOf(string entity) => _owners.GetValueOrDefault(entity);
}
