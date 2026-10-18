using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Auth;

public sealed partial class PlatformAuthService
{
    public const string Issuer = "BemAqui Plataforma";
    public const int RecoveryCodeCount = 10;

    /// <summary>Primeiro acesso: o desafio de configuração devolve o QR (sem opção "Fazer depois").</summary>
    public async Task<Result<PlatformTotpSetup>> BeginSetupAsync(string challenge, CancellationToken cancellationToken = default)
    {
        var (open, admin) = await OpenTokenAsync(challenge, PlatformTokenPurpose.Setup, cancellationToken);
        if (open is null || admin is null || admin.TotpEnabled)
        {
            return Error.Validation("auth.2fa_challenge_expired", ChallengeExpired);
        }

        var secret = Totp.NewSecret();
        admin.BeginTotpSetup(secrets.Protect(SecretScope.Platform, SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
        await db.SaveChangesAsync(cancellationToken);
        return new PlatformTotpSetup(Totp.ManualKey(secret), Totp.OtpAuthUri(Issuer, admin.Email, secret));
    }

    public async Task<Result<PlatformTwoFactorActivated>> ConfirmSetupAsync(string challenge, string code, string host, string? ip, CancellationToken cancellationToken = default)
    {
        var (open, admin) = await OpenTokenAsync(challenge, PlatformTokenPurpose.Setup, cancellationToken);
        if (open is null || admin is null || admin.TotpEnabled || admin.TotpSecretCipher is null)
        {
            return Error.Validation("auth.2fa_challenge_expired", ChallengeExpired);
        }

        var now = clock.GetUtcNow();
        if (Totp.Match(SecretOf(admin), code, now) is not { } step)
        {
            return (await RejectAsync(open, admin.Id, ip, cancellationToken)).Error!;
        }

        open.Use(now);
        admin.EnableTotp(step);
        await db.PlatformRecoveryCodes.Where(r => r.AdminId == admin.Id).ExecuteDeleteAsync(cancellationToken);
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        db.PlatformRecoveryCodes.AddRange(codes.Select(c => new PlatformRecoveryCode { AdminId = admin.Id, CodeHash = HashRecoveryCode(c), CreatedAt = now }));
        await AuditAsync("TwoFactorEnabled", admin.Id, ip, null, cancellationToken);
        return new PlatformTwoFactorActivated(codes, await NewSessionAsync(admin, host, ip, cancellationToken));
    }

    /// <summary>Desafio do login: código do app (anti-replay atômico) ou de recuperação (uso único); 5 erros derrubam.</summary>
    public async Task<Result<PlatformSessionIssued>> VerifyAsync(string challenge, string? code, string? recoveryCode, string host, string? ip, CancellationToken cancellationToken = default)
    {
        var (open, admin) = await OpenTokenAsync(challenge, PlatformTokenPurpose.Verify, cancellationToken);
        if (open is null || admin is null || !admin.TotpEnabled || admin.Status != PlatformAdminStatus.Active)
        {
            return Error.Validation("auth.2fa_challenge_expired", ChallengeExpired);
        }

        var usedRecovery = !string.IsNullOrWhiteSpace(recoveryCode);
        var accepted = usedRecovery ? await TryUseRecoveryCodeAsync(admin.Id, recoveryCode!, cancellationToken) : await TryAcceptTotpAsync(admin, code, cancellationToken);
        if (!accepted)
        {
            return (await RejectAsync(open, admin.Id, ip, cancellationToken)).Error!;
        }

        open.Use(clock.GetUtcNow());
        await AuditAsync(usedRecovery ? "RecoveryCodeUsed" : "TwoFactorVerified", admin.Id, ip, null, cancellationToken);
        return await NewSessionAsync(admin, host, ip, cancellationToken);
    }

    private async Task<PlatformSessionIssued> NewSessionAsync(PlatformAdmin admin, string host, string? ip, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var raw = OpaqueToken.New();
        db.PlatformSessions.Add(new PlatformSession { AdminId = admin.Id, TokenHash = OpaqueToken.Hash(raw), Host = host, CreatedAt = now, LastSeenAt = now, CreatedByIp = ip });
        admin.RegisterSuccessfulLogin(now);
        await AuditAsync("LoginSucceeded", admin.Id, ip, null, cancellationToken);
        return new PlatformSessionIssued(raw, now + Options.IdleTimeout);
    }

    private async Task<Result> RejectAsync(PlatformAdminToken challenge, Guid adminId, string? ip, CancellationToken cancellationToken)
    {
        var dropped = challenge.RegisterFailure(clock.GetUtcNow());
        await AuditAsync(dropped ? "TwoFactorChallengeFailed" : "TwoFactorCodeRejected", adminId, ip, new { attempts = challenge.Attempts }, cancellationToken);
        var n = challenge.RemainingAttempts;
        return dropped
            ? Error.Validation("auth.2fa_too_many_attempts", "Muitas tentativas com código errado. Entre de novo.")
            : Error.Validation("auth.2fa_invalid_code", $"Código inválido. Restam {n} tentativa{(n == 1 ? string.Empty : "s")}.");
    }

    private async Task<bool> TryAcceptTotpAsync(PlatformAdmin admin, string? code, CancellationToken cancellationToken)
    {
        if (admin.TotpSecretCipher is null || Totp.Match(SecretOf(admin), code, clock.GetUtcNow()) is not { } step)
        {
            return false;
        }

        return await db.PlatformAdmins
            .Where(a => a.Id == admin.Id && (a.TotpLastStep == null || a.TotpLastStep < step))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TotpLastStep, step), cancellationToken) == 1;
    }

    private async Task<bool> TryUseRecoveryCodeAsync(Guid adminId, string code, CancellationToken cancellationToken)
    {
        var hash = HashRecoveryCode(code);
        var now = clock.GetUtcNow();
        return await db.PlatformRecoveryCodes
            .Where(r => r.AdminId == adminId && r.CodeHash == hash && r.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken) == 1;
    }

    private byte[] SecretOf(PlatformAdmin admin) =>
        Base32.Decode(secrets.Unprotect(SecretScope.Platform, SecretKind.TotpSecret, admin.TotpSecretCipher!).Reveal());

    private static string NewRecoveryCode()
    {
        var raw = Base32.Encode(RandomNumberGenerator.GetBytes(8))[..10];
        return raw[..5] + "-" + raw[5..];
    }

    private static string HashRecoveryCode(string code) =>
        OpaqueToken.Hash(new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant());
}
