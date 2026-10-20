namespace BemAqui.BuildingBlocks.Identity;

/// <summary>
/// Consulta mínima de usuários para os outros módulos, sem acesso às tabelas do Identity (ADR 0006): quem é do grupo
/// Proprietário (para notificar trocas sensíveis, CAD-05b CA 9) e o nome de exibição de um usuário.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Usuários ativos membros do grupo Proprietário.</summary>
    Task<IReadOnlyList<Guid>> OwnerIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Todos os usuários ativos do tenant (avisos de jobs, filtrados pela permissão efetiva de cada um).</summary>
    Task<IReadOnlyList<Guid>> ActiveUserIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Usuários ativos com a permissão efetiva (ex.: "finance.approve"), já limitada pelos módulos liberados.</summary>
    Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(string permission, CancellationToken cancellationToken = default);

    Task<string?> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default);
}
