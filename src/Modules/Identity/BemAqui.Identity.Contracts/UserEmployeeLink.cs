using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>Usuário para o vínculo com o funcionário (CAD-04b CA 19 e 20).</summary>
public sealed record LinkableUser(Guid UserId, string Name, string Email);

/// <summary>
/// Vínculo usuário ↔ funcionário (CAD-04b CA 19 e 20), gravado em <c>identity.users.employee_id</c> pelo Identity. Não muda
/// grupo nem permissão; quem chama confere <c>hr.edit</c> e <c>identity.edit</c>.
/// </summary>
public interface IUserEmployeeLink
{
    Task<LinkableUser?> UserOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Usuários ativos ainda sem funcionário.</summary>
    Task<IReadOnlyList<LinkableUser>> LinkableAsync(CancellationToken cancellationToken = default);

    Task<Result<LinkableUser>> LinkAsync(Guid userId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<Result> UnlinkAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
