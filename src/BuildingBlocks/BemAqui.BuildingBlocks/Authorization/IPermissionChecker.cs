namespace BemAqui.BuildingBlocks.Authorization;

/// <summary>
/// Permissão efetiva do usuário atual (<c>modulo.acao</c>, ADRs 0002, 0003 e 0008). A implementação real chega
/// na FND-06c; até lá vale <see cref="DenyAllPermissionChecker"/> (falha fechada).
/// </summary>
public interface IPermissionChecker
{
    ValueTask<bool> HasPermissionAsync(string module, string action, CancellationToken cancellationToken = default);
}

public sealed class DenyAllPermissionChecker : IPermissionChecker
{
    public ValueTask<bool> HasPermissionAsync(string module, string action, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}

public static class StandardActions
{
    public const string View = "view";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Approve = "approve";
    public const string Delete = "delete";
}
