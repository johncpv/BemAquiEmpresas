using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed record LoginSession(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Senha certa, falta o 2FA: o desafio é um token opaco de 5 min, só aceito pelas rotas de 2FA.</summary>
public sealed record TwoFactorPending(string Challenge, TwoFactorChallengePurpose Purpose, DateTimeOffset ExpiresAt);

/// <summary>Resultado do login: a sessão completa ou o passo de 2FA pendente (FND-05 CA 3 e 8).</summary>
public sealed record LoginOutcome(LoginSession? Session, TwoFactorPending? TwoFactor);

/// <summary>
/// Login, renovação e saída (FND-04a). Falha de login responde sempre igual ("E-mail ou senha inválidos."),
/// com tempo equivalente, para não revelar se o e-mail existe (CA 10). Eventos de segurança vão para a auditoria.
/// </summary>
public sealed partial class AuthService(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    IAccessTokenIssuer issuer,
    ITenantContext tenant,
    TokenSigning signing,
    IEmailSender mail,
    TenantLinks links,
    TwoFactorRequirement requirement,
    TimeProvider clock)
{
    public const string InvalidCredentials = "E-mail ou senha inválidos.";
    public const string SessionExpired = "Sua sessão expirou. Entre de novo.";

    // Hash de uma senha qualquer: usado quando o usuário não existe, para o tempo de resposta não denunciar.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(User.Invite("ninguem@exemplo.com"), "senha-que-nao-existe-123");

    public async Task<Result<LoginOutcome>> LoginAsync(string email, string password, string host, string? ip, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var normalized = User.Normalize(email ?? string.Empty);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user?.PasswordHash is null || user.Status != UserStatus.Active)
        {
            hasher.VerifyHashedPassword(User.Invite("ninguem@exemplo.com"), DummyHash, password ?? string.Empty);
            await AuditAsync("LoginFailed", user?.Id, ip, new { reason = user is null ? "unknown_email" : "not_active" }, cancellationToken);
            return Invalid();
        }

        if (user.IsLockedOut(now))
        {
            hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
            await AuditAsync("LoginBlocked", user.Id, ip, new { lockoutEnd = user.LockoutEnd }, cancellationToken);
            return Invalid();
        }

        var verified = hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verified == PasswordVerificationResult.Failed)
        {
            var lockedNow = user.RegisterFailedLogin(now);
            if (lockedNow)
            {
                // Avisa o dono da conta: se não foi ele, o link leva direto à redefinição (propositivo).
                mail.Enqueue("account-locked", user.Email, new Dictionary<string, object?>
                {
                    ["locked_until"] = user.LockoutEnd,
                    ["link"] = links.Absolute("/esqueci-senha"),
                }, "User", user.Id);
            }

            await AuditAsync(lockedNow ? "LockedOut" : "LoginFailed", user.Id, ip, new { reason = "wrong_password" }, cancellationToken);
            return Invalid();
        }

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPassword(hasher.HashPassword(user, password!));
        }

        if (user.TotpEnabled || await requirement.IsRequiredAsync(user, cancellationToken))
        {
            user.RegisterPasswordVerified();
            var purpose = user.TotpEnabled ? TwoFactorChallengePurpose.Verify : TwoFactorChallengePurpose.Setup;
            var pending = NewChallenge(user.Id, purpose, ip, now);
            await AuditAsync(purpose == TwoFactorChallengePurpose.Verify ? "TwoFactorChallenged" : "TwoFactorSetupRequired", user.Id, ip, null, cancellationToken);
            return new LoginOutcome(null, pending);
        }

        return new LoginOutcome(await StartSessionAsync(user, host, ip, cancellationToken), null);
    }

    /// <summary>Sessão completa (access + refresh numa família nova), depois da senha e, se houver, do 2FA.</summary>
    public async Task<LoginSession> StartSessionAsync(User user, string host, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = clock.GetUtcNow();
        user.RegisterSuccessfulLogin(now);
        var session = CreateSession(user, host, Guid.CreateVersion7(), ip, now);
        await AuditAsync("LoginSucceeded", user.Id, ip, null, cancellationToken);
        return session;
    }

    private TwoFactorPending NewChallenge(Guid userId, TwoFactorChallengePurpose purpose, string? ip, DateTimeOffset now)
    {
        var raw = OpaqueToken.New();
        var challenge = new TwoFactorChallenge
        {
            UserId = userId,
            TokenHash = OpaqueToken.Hash(raw),
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now + TwoFactorChallenge.Lifetime,
            CreatedByIp = ip,
        };
        db.TwoFactorChallenges.Add(challenge);
        return new TwoFactorPending(raw, purpose, challenge.ExpiresAt);
    }

    public async Task LogoutAsync(string? refreshToken, string? ip, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var hash = OpaqueToken.Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return;
        }

        await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), cancellationToken);
        await AuditAsync("Logout", token.UserId, ip, null, cancellationToken);
    }

    /// <summary>Revoga todas as sessões do usuário (redefinição de senha, desativação).</summary>
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        foreach (var token in await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken))
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private LoginSession CreateSession(User user, string host, Guid familyId, string? ip, DateTimeOffset now)
    {
        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Login fora de escopo de tenant.");
        var access = issuer.Issue(user, tenantId, host);
        var raw = OpaqueToken.New();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = OpaqueToken.Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now + signing.Options.RefreshTokenLifetime,
            CreatedByIp = ip,
        };
        db.RefreshTokens.Add(refresh);
        return new LoginSession(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var token in await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(cancellationToken))
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task AuditAsync(string action, Guid? userId, string? ip, object? details, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "Auth",
            EntityId = userId?.ToString() ?? "-",
            Action = action,
            UserId = userId,
            At = clock.GetUtcNow(),
            Ip = ip,
            Changes = details is null ? "{}" : JsonSerializer.Serialize(details),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Error Invalid() => Error.Validation("auth.invalid_credentials", InvalidCredentials);
}
