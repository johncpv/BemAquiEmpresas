using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.Identity.Application;

/// <summary>
/// Permissão do usuário do request (substitui o DenyAll da FND-07): histórico de auditoria e anexos passam a valer
/// pela permissão efetiva, limitada aos módulos liberados (FND-06c).
/// </summary>
public sealed class CurrentUserPermissionChecker(ICurrentUser user, IPermissionService permissions) : IPermissionChecker
{
    public async ValueTask<bool> HasPermissionAsync(string module, string action, CancellationToken cancellationToken = default) =>
        user.UserId is { } id && (await permissions.GetAsync(id, cancellationToken)).Has(module + "." + action);
}
