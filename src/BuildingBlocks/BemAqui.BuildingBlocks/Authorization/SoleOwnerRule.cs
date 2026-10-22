using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Authorization;

/// <summary>
/// Regra do Proprietário único (D6 de 20/10/2026, ADR 0023 §5, ponto 76 da revisão pré-Fase 3): quem fez não aprova o que fez,
/// <b>exceto</b> quando não existe outro aprovador elegível, isto é, nenhum outro usuário ativo com a permissão que não esteja
/// envolvido no documento. Aí a ação passa com a marca "Autoaprovado" e o Proprietário é notificado (quem chama grava a marca e
/// a notificação). Criado na PRD-03a e reaproveitado pela EST-01a, EST-01b, CMP-03a, CMP-04 e PRD-05.
/// </summary>
public static class SoleOwnerRule
{
    public const string SelfApprovedLabel = "Autoaprovado";

    /// <summary>
    /// Sem banco: true quando nenhum aprovador além de <paramref name="actor"/> fica de fora de <paramref name="involved"/>,
    /// ou seja, a autoaprovação é permitida.
    /// </summary>
    public static bool NoOtherEligible(IEnumerable<Guid> approvers, Guid actor, IReadOnlyCollection<Guid> involved)
    {
        ArgumentNullException.ThrowIfNull(approvers);
        ArgumentNullException.ThrowIfNull(involved);
        return !approvers.Any(a => a != actor && !involved.Contains(a));
    }

    /// <summary>Pelos usuários ativos com a permissão efetiva (ex.: "production.approve"), já limitada pelos módulos liberados.</summary>
    public static async Task<bool> NoOtherEligibleAsync(IUserDirectory users, string permission, Guid actor, IReadOnlyCollection<Guid> involved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(users);
        return NoOtherEligible(await users.UsersWithPermissionAsync(permission, cancellationToken), actor, involved);
    }
}