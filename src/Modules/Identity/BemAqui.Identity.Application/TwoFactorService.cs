using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Dados para o app autenticador: chave manual e URI do QR code (o QR é desenhado no cliente).</summary>
public sealed record TotpSetup(string ManualKey, string OtpAuthUri);

/// <summary>2FA ativado: os códigos de recuperação aparecem só agora; a sessão é nova porque o stamp mudou.</summary>
public sealed record TwoFactorActivated(IReadOnlyList<string> RecoveryCodes, LoginSession Session);

/// <summary>
/// Verificação em duas etapas (FND-05): ativação com confirmação, códigos de recuperação de uso único, desafio no
/// login com anti-replay e limite de tentativas, e desativação quando não é obrigatória.
/// </summary>
public sealed partial class TwoFactorService(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    ISecretProtector secrets,
    ITenantContext tenant,
    TwoFactorRequirement requirement,
    AuthService auth,
    ISecurityStampCache stamps,
    TimeProvider clock)
{
    public const string Issuer = "BemAqui Empresas";
    public const string ChallengeExpired = "O tempo para informar o código acabou. Entre de novo.";
    public const string TooManyAttempts = "Muitas tentativas com código errado. Entre de novo.";
    public const string AlreadyEnabled = "A verificação em duas etapas já está ativa.";
    public const string SetupNotStarted = "Comece a ativação de novo para gerar o QR code.";
    public const string RequiredCannotDisable = "A verificação em duas etapas é obrigatória para você e não pode ser desativada.";
    public const string InvalidCredentials = "Senha ou código inválido.";

    public async Task<Result<TotpSetup>> BeginSetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        if (user.TotpEnabled)
        {
            return Error.Conflict("auth.2fa_already_enabled", AlreadyEnabled);
        }

        var secret = Totp.NewSecret();
        user.BeginTotpSetup(secrets.Protect(Scope(), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
        await db.SaveChangesAsync(cancellationToken);
        return new TotpSetup(Totp.ManualKey(secret), Totp.OtpAuthUri(Issuer, user.Email, secret));
    }

    public Task<Result<TwoFactorActivated>> ConfirmSetupAsync(Guid userId, string code, string host, string? ip, CancellationToken cancellationToken = default) =>
        ConfirmSetupAsync(userId, code, host, ip, persistent: true, cancellationToken);

    /// <param name="persistent">false quando a ativação obrigatória acontece no login feito no tablet da fábrica (ADR 0026 §2).</param>
    public async Task<Result<TwoFactorActivated>> ConfirmSetupAsync(Guid userId, string code, string host, string? ip, bool persistent, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        if (user.TotpEnabled)
        {
            return Error.Conflict("auth.2fa_already_enabled", AlreadyEnabled);
        }

        if (user.TotpSecretCipher is null)
        {
            return Error.Validation("auth.2fa_setup_not_started", SetupNotStarted);
        }

        if (Totp.Match(SecretOf(user), code, clock.GetUtcNow()) is not { } step)
        {
            return InvalidCode(null);
        }

        var now = clock.GetUtcNow();
        user.EnableTotp(step);
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        var codes = Enumerable.Range(0, RecoveryCode.CountPerUser).Select(_ => NewRecoveryCode()).ToList();
        db.RecoveryCodes.AddRange(codes.Select(c => new RecoveryCode { UserId = user.Id, CodeHash = HashRecoveryCode(c), CreatedAt = now }));
        await auth.AuditAsync("TwoFactorEnabled", user.Id, ip, null, cancellationToken);
        return new TwoFactorActivated(codes, await NewSessionAsync(user, host, ip, persistent, cancellationToken));
    }

    public async Task<Result<LoginSession>> DisableAsync(Guid userId, string password, string code, string host, string? ip, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUserAsync(userId, cancellationToken);
        if (user is null || !user.TotpEnabled)
        {
            return Error.Validation("auth.2fa_not_enabled", "A verificação em duas etapas não está ativa.");
        }

        if (await requirement.IsRequiredAsync(user, cancellationToken))
        {
            return Error.Forbidden("auth.2fa_required", RequiredCannotDisable);
        }

        var passwordOk = user.PasswordHash is not null && hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty) != PasswordVerificationResult.Failed;
        if (!passwordOk || !await TryAcceptTotpAsync(user, code, cancellationToken))
        {
            await auth.AuditAsync("TwoFactorDisableRejected", user.Id, ip, null, cancellationToken);
            return Error.Validation("auth.2fa_invalid", InvalidCredentials);
        }

        user.DisableTotp();
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await auth.AuditAsync("TwoFactorDisabled", user.Id, ip, null, cancellationToken);
        return await NewSessionAsync(user, host, ip, persistent: true, cancellationToken);
    }
}
