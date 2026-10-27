using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class TwoFactorService
{
    /// <summary>Desafio do login (CA 4 a 7): código do app ou de recuperação. Cinco erros derrubam o desafio.</summary>
    public Task<Result<LoginSession>> VerifyAsync(string challenge, string? code, string? recoveryCode, string host, string? ip, CancellationToken cancellationToken = default) =>
        VerifyAsync(challenge, code, recoveryCode, host, ip, persistent: true, cancellationToken);

    /// <param name="persistent">false no login completo feito no tablet da fábrica: sem refresh (ADR 0026 §2).</param>
    public async Task<Result<LoginSession>> VerifyAsync(string challenge, string? code, string? recoveryCode, string host, string? ip, bool persistent, CancellationToken cancellationToken = default)
    {
        var (open, user) = await OpenChallengeAsync(challenge, TwoFactorChallengePurpose.Verify, cancellationToken);
        if (open is null || user is null || !user.TotpEnabled)
        {
            return Error.Validation("auth.2fa_challenge_expired", ChallengeExpired);
        }

        var usedRecovery = !string.IsNullOrWhiteSpace(recoveryCode);
        var accepted = usedRecovery
            ? await TryUseRecoveryCodeAsync(user.Id, recoveryCode!, cancellationToken)
            : await TryAcceptTotpAsync(user, code, cancellationToken);
        if (!accepted)
        {
            return await RejectAsync(open, user.Id, ip, cancellationToken);
        }

        open.Complete(clock.GetUtcNow());
        await auth.AuditAsync(usedRecovery ? "RecoveryCodeUsed" : "TwoFactorVerified", user.Id, ip, null, cancellationToken);
        // Com o código do aplicativo, o token já sai com a prova recente do 2FA (BNK-01a CA 1); com o código de recuperação, não.
        return await auth.StartSessionAsync(user, host, ip, [AuthMethods.Password, AuthMethods.Otp], persistent, otpProven: !usedRecovery, cancellationToken);
    }

    /// <summary>2FA obrigatório e ainda não configurado (CA 8): o desafio de configuração só libera estas duas etapas.</summary>
    public async Task<Result<TotpSetup>> BeginSetupWithChallengeAsync(string challenge, CancellationToken cancellationToken = default)
    {
        var (open, user) = await OpenChallengeAsync(challenge, TwoFactorChallengePurpose.Setup, cancellationToken);
        return open is null || user is null
            ? Error.Validation("auth.2fa_challenge_expired", ChallengeExpired)
            : await BeginSetupAsync(user.Id, cancellationToken);
    }

    public Task<Result<TwoFactorActivated>> ConfirmSetupWithChallengeAsync(string challenge, string code, string host, string? ip, CancellationToken cancellationToken = default) =>
        ConfirmSetupWithChallengeAsync(challenge, code, host, ip, persistent: true, cancellationToken);

    public async Task<Result<TwoFactorActivated>> ConfirmSetupWithChallengeAsync(string challenge, string code, string host, string? ip, bool persistent, CancellationToken cancellationToken = default)
    {
        var (open, user) = await OpenChallengeAsync(challenge, TwoFactorChallengePurpose.Setup, cancellationToken);
        if (open is null || user is null)
        {
            return Error.Validation("auth.2fa_challenge_expired", ChallengeExpired);
        }

        var result = await ConfirmSetupAsync(user.Id, code, host, ip, persistent, cancellationToken);
        if (result.IsFailure && result.Error!.Code == "auth.2fa_invalid_code")
        {
            return (await RejectAsync(open, user.Id, ip, cancellationToken)).Error!;
        }

        if (result.IsSuccess)
        {
            open.Complete(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private async Task<(TwoFactorChallenge? Challenge, User? User)> OpenChallengeAsync(string token, TwoFactorChallengePurpose purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return (null, null);
        }

        var hash = OpaqueToken.Hash(token);
        var challenge = await db.TwoFactorChallenges.FirstOrDefaultAsync(c => c.TokenHash == hash && c.Purpose == purpose, cancellationToken);
        if (challenge is null || !challenge.IsOpen(clock.GetUtcNow()))
        {
            return (null, null);
        }

        return (challenge, await ActiveUserAsync(challenge.UserId, cancellationToken));
    }

    private async Task<Result<LoginSession>> RejectAsync(TwoFactorChallenge challenge, Guid userId, string? ip, CancellationToken cancellationToken)
    {
        var dropped = challenge.RegisterFailure(clock.GetUtcNow());
        await auth.AuditAsync(dropped ? "TwoFactorChallengeFailed" : "TwoFactorCodeRejected", userId, ip, new { attempts = challenge.Attempts }, cancellationToken);
        return dropped
            ? Error.Validation("auth.2fa_too_many_attempts", TooManyAttempts)
            : InvalidCode(challenge.RemainingAttempts);
    }

    /// <summary>Anti-replay atômico (CA 5): só aceita se o intervalo for maior que o último aceito, num UPDATE condicional.</summary>
    private async Task<bool> TryAcceptTotpAsync(User user, string? code, CancellationToken cancellationToken)
    {
        if (user.TotpSecretCipher is null || Totp.Match(SecretOf(user), code, clock.GetUtcNow()) is not { } step)
        {
            return false;
        }

        var updated = await db.Users
            .Where(u => u.Id == user.Id && (u.TotpLastStep == null || u.TotpLastStep < step))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpLastStep, step), cancellationToken);
        return updated == 1;
    }

    /// <summary>Uso único (CA 7), também num UPDATE condicional para duas requisições não usarem o mesmo código.</summary>
    private async Task<bool> TryUseRecoveryCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var hash = HashRecoveryCode(code);
        var now = clock.GetUtcNow();
        var updated = await db.RecoveryCodes
            .Where(r => r.UserId == userId && r.CodeHash == hash && r.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken);
        return updated == 1;
    }

    private async Task<LoginSession> NewSessionAsync(User user, string host, string? ip, bool persistent, CancellationToken cancellationToken)
    {
        await auth.RevokeAllAsync(user.Id, cancellationToken);
        stamps.Invalidate(tenant.Tenant!.Id, user.Id);
        var amr = user.TotpEnabled ? new[] { AuthMethods.Password, AuthMethods.Otp } : [AuthMethods.Password];
        return await auth.StartSessionAsync(user, host, ip, amr, persistent, cancellationToken);
    }

    private Task<User?> ActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);

    private byte[] SecretOf(User user) =>
        Base32.Decode(secrets.Unprotect(Scope(), SecretKind.TotpSecret, user.TotpSecretCipher!).Reveal());

    private SecretScope Scope() => SecretScope.ForTenant(tenant.Tenant?.Id ?? throw new InvalidOperationException("2FA fora de escopo de tenant."));

    private static Error InvalidCode(int? remaining) => Error.Validation(
        "auth.2fa_invalid_code",
        remaining is { } n ? $"Código inválido. Restam {n} tentativa{(n == 1 ? string.Empty : "s")}." : "Código inválido. Confira o código no app e tente de novo.");

    /// <summary>10 caracteres do Base32 (50 bits), no formato XXXXX-XXXXX.</summary>
    private static string NewRecoveryCode()
    {
        var raw = Base32.Encode(RandomNumberGenerator.GetBytes(8))[..10];
        return raw[..5] + "-" + raw[5..];
    }

    private static string HashRecoveryCode(string code) =>
        OpaqueToken.Hash(new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant());
}
