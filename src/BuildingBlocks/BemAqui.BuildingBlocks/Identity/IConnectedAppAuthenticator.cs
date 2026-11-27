using BemAqui.BuildingBlocks.Security;

namespace BemAqui.BuildingBlocks.Identity;

public enum AppTokenStatus
{
    Invalid,
    Valid,

    /// <summary>Quem autorizou foi desativado ou desligado: a conexão fica suspensa (INT-01 CA 17).</summary>
    GrantorInactive,
}

public sealed record AppTokenResult(AppTokenStatus Status, ConnectedAppGrant? Grant)
{
    public static AppTokenResult Invalid { get; } = new(AppTokenStatus.Invalid, null);
}

/// <summary>Acha o token de acesso de aplicativo pelo hash (cache curto, invalidado na revogação, ADR 0053 §11).</summary>
public interface IConnectedAppAuthenticator
{
    Task<AppTokenResult> AuthenticateAsync(string accessToken, string? ip, CancellationToken cancellationToken = default);
}
