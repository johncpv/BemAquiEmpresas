using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Situação do 2FA para a tela do perfil: se não puder desativar, a opção nem aparece (CA 10).</summary>
public sealed record TwoFactorStatus(bool Enabled, bool Required, int RecoveryCodesLeft);

public sealed partial class TwoFactorService
{
    public async Task<TwoFactorStatus?> StatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var left = await db.RecoveryCodes.CountAsync(r => r.UserId == userId && r.UsedAt == null, cancellationToken);
        return new TwoFactorStatus(user.TotpEnabled, await requirement.IsRequiredAsync(user, cancellationToken), left);
    }
}
