using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.Identity.Application;

/// <summary>
/// "2FA de novo" antes de ação sensível (D6, FND-16 CA 7): exige o 2FA ativo e um código do aplicativo ainda não usado (o mesmo
/// anti-replay do login). Recusa e aceite ficam na auditoria (<c>Auth</c>), sem o código.
/// </summary>
public sealed partial class TwoFactorService : IStepUpTwoFactor
{
    public async Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null || !user.TotpEnabled || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var accepted = await TryAcceptTotpAsync(user, code.Trim(), cancellationToken);
        await auth.AuditAsync(accepted ? "TwoFactorStepUp" : "TwoFactorStepUpRejected", user.Id, null, null, cancellationToken);
        return accepted;
    }
}
