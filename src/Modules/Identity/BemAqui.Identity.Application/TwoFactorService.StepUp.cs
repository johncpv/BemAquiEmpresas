using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application;

/// <summary>
/// "2FA de novo" antes de ação sensível (D6, FND-16 CA 7): exige o 2FA ativo e um código do aplicativo ainda não usado (o mesmo
/// anti-replay do login). Recusa e aceite ficam na auditoria (<c>Auth</c>), sem o código.
/// </summary>
public sealed partial class TwoFactorService : IStepUpTwoFactor
{
    public const string StepUpInvalidCode = "Código inválido ou já usado. Confira o aplicativo autenticador e tente de novo.";

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

    /// <summary>
    /// Renova a prova do 2FA (BNK-01a CA 1): confere o código e devolve só um access token novo com <c>otp_at</c> agora; a sessão
    /// (família de refresh e cookie) não muda. Sem 2FA ativo, 403 <c>two_factor_required</c> com o texto de ativar.
    /// </summary>
    public async Task<Result<AccessToken>> StepUpAsync(Guid userId, string? code, string host, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null || !user.TotpEnabled)
        {
            return Error.Forbidden(RecentTwoFactorEndpoints.Code, RecentTwoFactorEndpoints.EnrollMessage);
        }

        if (!await VerifyAsync(userId, code, cancellationToken))
        {
            return Error.Validation("auth.2fa_invalid_code", StepUpInvalidCode, new Dictionary<string, string[]> { ["code"] = [StepUpInvalidCode] });
        }

        return auth.IssueStepUp(user, host);
    }
}
