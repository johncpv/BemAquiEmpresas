using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>
/// Usuário para o vínculo com o funcionário (CAD-04b CA 19 e 20). <c>Status</c> é a situação do login (<c>Active</c>, <c>Invited</c> ou
/// <c>Inactive</c>, USR-01): o funcionário pode estar reservado por um convite ainda não aceito.
/// </summary>
public sealed record LinkableUser(Guid UserId, string Name, string Email, string Status = "Active");

/// <summary>O que o desligamento fez com o login do funcionário (USR-01 CA 9 a 12).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationLoginOutcome>))]
public enum TerminationLoginOutcome
{
    /// <summary>Funcionário sem usuário vinculado.</summary>
    NoUser,

    /// <summary>Login desativado agora: sessões e PIN do chão de fábrica caem no próximo request.</summary>
    Deactivated,

    /// <summary>O login já estava desativado.</summary>
    AlreadyInactive,

    /// <summary>Não desativado: é o último membro ativo do grupo Proprietário.</summary>
    KeptLastOwner,

    /// <summary>Não desativado: é do grupo Proprietário e quem desligou não é (delegação, ADR 0008).</summary>
    KeptOwner,
}

/// <summary>Resultado do desligamento no login (USR-01): a mensagem já vem pronta para a conclusão.</summary>
public sealed record TerminationLogin(TerminationLoginOutcome Outcome, Guid? UserId, string? UserName, string? Message)
{
    public static readonly TerminationLogin None = new(TerminationLoginOutcome.NoUser, null, null, null);
}

/// <summary>
/// Vínculo usuário ↔ funcionário (CAD-04b CA 19 e 20), gravado em <c>identity.users.employee_id</c> pelo Identity. Não muda
/// grupo nem permissão; quem chama confere <c>hr.edit</c> e <c>identity.edit</c>.
/// </summary>
public interface IUserEmployeeLink
{
    Task<LinkableUser?> UserOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Funcionário ligado ao usuário (PRD-03a, ponto 67): o apontamento sempre resolve o funcionário por aqui, nunca pelo corpo
    /// do pedido. Null se o usuário não existe, não está ativo ou não tem funcionário.
    /// </summary>
    Task<Guid?> EmployeeOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Usuários ativos ainda sem funcionário.</summary>
    Task<IReadOnlyList<LinkableUser>> LinkableAsync(CancellationToken cancellationToken = default);

    /// <summary>Funcionários que já têm usuário (ativo, convidado ou desativado), para o convite oferecer só os livres (USR-01 CA 1).</summary>
    Task<IReadOnlyCollection<Guid>> LinkedEmployeeIdsAsync(CancellationToken cancellationToken = default);

    Task<Result<LinkableUser>> LinkAsync(Guid userId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<Result> UnlinkAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Desligamento efetivado (USR-01 CA 9 a 12): desativa o login vinculado pelo mesmo caminho da desativação manual (sessões derrubadas,
    /// PIN sem efeito), com auditoria. O Proprietário segue as regras do grupo: o último membro ativo nunca é desativado, e só um Proprietário
    /// desativa outro. Quem chama já gravou o desligamento; nada aqui desfaz o desligamento.
    /// </summary>
    Task<TerminationLogin> DeactivateForTerminationAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
