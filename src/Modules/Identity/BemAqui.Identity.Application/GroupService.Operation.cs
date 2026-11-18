using BemAqui.Identity.Application.Shopfloor;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class GroupService
{
    public const string OperationUserGroupCode = "operation_user_sensitive_group";

    /// <summary>
    /// ADR 0049: o usuário de operação nunca recebe ação reservada (<c>*.approve</c>, <c>finance.pay</c>, <c>hr.view_sensitive</c>,
    /// <c>hr.view_health</c>, <c>identity.edit</c>, <c>settings.edit</c> ou o Proprietário): grupo que tem alguma delas não aceita usuário de operação.
    /// Devolve a primeira ação reservada dos grupos, ou null.
    /// </summary>
    internal async Task<string?> ReservedActionOfGroupsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToList();
        foreach (var group in await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken))
        {
            if (group.IsOwner)
            {
                return "owner";
            }

            if ((await PermissionsOfGroupAsync(group, cancellationToken)).Order(StringComparer.Ordinal).FirstOrDefault(PinProfile.IsSensitive) is { } reserved)
            {
                return reserved;
            }
        }

        return null;
    }

    /// <summary>Recusa pôr usuário de operação em grupo com ação reservada (convite de membros, grupos do usuário, criação).</summary>
    internal async Task<Error?> OperationUsersDeniedAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0 || groupIds.Count == 0 || !await db.Users.AnyAsync(u => userIds.Contains(u.Id) && u.IsOperationUser, cancellationToken))
        {
            return null;
        }

        return await ReservedActionOfGroupsAsync(groupIds, cancellationToken) is { } reserved ? OperationUserDenied(reserved) : null;
    }

    internal static Error OperationUserDenied(string reserved) =>
        Error.Validation(OperationUserGroupCode, reserved == "owner"
            ? "Usuário de operação não entra no grupo Proprietário: ele só usa o caixa."
            : $"Usuário de operação não entra em grupo com ação reservada ({reserved}): ele só usa o caixa. Escolha outro grupo ou tire a ação do grupo.");
}
