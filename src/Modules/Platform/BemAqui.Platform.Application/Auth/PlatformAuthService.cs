using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Platform.Application.Auth;

/// <summary>
/// Login do super admin (FND-13): senha com as regras da FND-04 (resposta uniforme, bloqueio de 15 min) e 2FA
/// sempre obrigatório. Sem código, não há sessão (CA 3). Tudo é auditado no catálogo com IP (CA 8).
/// </summary>
public sealed partial class PlatformAuthService(
    IPlatformAuthDb db,
    IPasswordHasher<PlatformAdmin> hasher,
    ISecretProtector secrets,
    IOptions<PlatformAuthOptions> options,
    TimeProvider clock)
{
    public const string InvalidCredentials = "E-mail ou senha inválidos.";
    public const string LinkExpired = "Link expirado ou já usado. Peça um novo pela CLI.";
    public const string ChallengeExpired = "O tempo para informar o código acabou. Entre de novo.";

    private static readonly string DummyHash = new PasswordHasher<PlatformAdmin>().HashPassword(PlatformAdmin.Create("ninguem@exemplo.com", DateTimeOffset.UnixEpoch), "senha-que-nao-existe-123");

    private PlatformAuthOptions Options => options.Value;

    /// <summary>CLI <c>platform-admin create</c> (CA 1): cria o convidado, ou gera um link novo se ainda não definiu a senha.</summary>
    public async Task<Result<PlatformAdminInvited>> InviteAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _))
        {
            return Error.Validation(Error.Codes.Validation, "Informe um e-mail válido.");
        }

        var now = clock.GetUtcNow();
        var normalized = PlatformAdmin.Normalize(email);
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.NormalizedEmail == normalized, cancellationToken);
        if (admin is null)
        {
            admin = PlatformAdmin.Create(email, now);
            db.PlatformAdmins.Add(admin);
        }
        else if (admin.Status != PlatformAdminStatus.Invited)
        {
            return Error.Conflict(Error.Codes.Conflict, "Esse super admin já definiu a senha.");
        }

        foreach (var old in await db.PlatformAdminTokens.Where(t => t.AdminId == admin.Id && t.Purpose == PlatformTokenPurpose.SetPassword && t.UsedAt == null).ToListAsync(cancellationToken))
        {
            old.Invalidate(now);
        }

        var (raw, token) = NewToken(admin.Id, PlatformTokenPurpose.SetPassword, Options.SetPasswordLinkLifetime, now);
        await AuditAsync("AdminInvited", admin.Id, null, null, cancellationToken);
        return new PlatformAdminInvited(admin.Id, raw, token.ExpiresAt);
    }

    public async Task<Result> SetPasswordAsync(string token, string password, string? ip, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var (open, admin) = await OpenTokenAsync(token, PlatformTokenPurpose.SetPassword, cancellationToken);
        if (open is null || admin is null)
        {
            return Error.Validation("auth.link_expired", LinkExpired);
        }

        if (PasswordPolicy.Validate(password, admin.Email) is { } problem)
        {
            return Error.Validation("auth.weak_password", problem, new Dictionary<string, string[]> { ["password"] = [problem] });
        }

        open.Use(now);
        admin.SetPassword(hasher.HashPassword(admin, password));
        await AuditAsync("PasswordSet", admin.Id, ip, null, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PlatformChallenge>> LoginAsync(string email, string password, string? ip, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var normalized = PlatformAdmin.Normalize(email ?? string.Empty);
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.NormalizedEmail == normalized, cancellationToken);
        if (admin?.PasswordHash is null || admin.Status != PlatformAdminStatus.Active)
        {
            hasher.VerifyHashedPassword(PlatformAdmin.Create("ninguem@exemplo.com", now), DummyHash, password ?? string.Empty);
            await AuditAsync("LoginFailed", admin?.Id, ip, new { reason = admin is null ? "unknown_email" : "not_active" }, cancellationToken);
            return Invalid();
        }

        if (admin.IsLockedOut(now))
        {
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, password ?? string.Empty);
            await AuditAsync("LoginBlocked", admin.Id, ip, null, cancellationToken);
            return Invalid();
        }

        if (hasher.VerifyHashedPassword(admin, admin.PasswordHash, password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            var locked = admin.RegisterFailedLogin(now);
            await AuditAsync(locked ? "LockedOut" : "LoginFailed", admin.Id, ip, new { reason = "wrong_password" }, cancellationToken);
            return Invalid();
        }

        admin.RegisterPasswordVerified();
        var purpose = admin.TotpEnabled ? PlatformTokenPurpose.Verify : PlatformTokenPurpose.Setup;
        var (raw, challenge) = NewToken(admin.Id, purpose, Options.ChallengeLifetime, now);
        await AuditAsync(purpose == PlatformTokenPurpose.Verify ? "TwoFactorChallenged" : "TwoFactorSetupRequired", admin.Id, ip, null, cancellationToken);
        return new PlatformChallenge(raw, purpose, challenge.ExpiresAt);
    }

    public async Task LogoutAsync(string? sessionToken, string? ip, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionToken))
        {
            return;
        }

        var hash = OpaqueToken.Hash(sessionToken);
        var session = await db.PlatformSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(clock.GetUtcNow());
        await AuditAsync("Logout", session.AdminId, ip, null, cancellationToken);
    }

    /// <summary>
    /// Valida o cookie da sessão (esquema <c>Platform</c>): ativa, no mesmo host, admin ativo e stamp igual. Renova a
    /// inatividade no máximo uma vez por minuto, para não gravar a cada request.
    /// </summary>
    public async Task<PlatformAdmin?> AuthenticateAsync(string sessionToken, string host, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var hash = OpaqueToken.Hash(sessionToken);
        var session = await db.PlatformSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null || !session.IsActive(now, Options.IdleTimeout, Options.AbsoluteTimeout) || !string.Equals(session.Host, host, StringComparison.Ordinal))
        {
            return null;
        }

        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == session.AdminId && a.Status == PlatformAdminStatus.Active && a.TotpEnabled, cancellationToken);
        if (admin is null)
        {
            return null;
        }

        if (now - session.LastSeenAt > TimeSpan.FromMinutes(1))
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return admin;
    }

    private (string Raw, PlatformAdminToken Token) NewToken(Guid adminId, PlatformTokenPurpose purpose, TimeSpan lifetime, DateTimeOffset now)
    {
        var raw = OpaqueToken.New();
        var token = new PlatformAdminToken { AdminId = adminId, TokenHash = OpaqueToken.Hash(raw), Purpose = purpose, CreatedAt = now, ExpiresAt = now + lifetime };
        db.PlatformAdminTokens.Add(token);
        return (raw, token);
    }

    private async Task<(PlatformAdminToken? Token, PlatformAdmin? Admin)> OpenTokenAsync(string raw, PlatformTokenPurpose purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (null, null);
        }

        var hash = OpaqueToken.Hash(raw);
        var token = await db.PlatformAdminTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, cancellationToken);
        if (token is null || !token.IsOpen(clock.GetUtcNow()))
        {
            return (null, null);
        }

        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == token.AdminId && a.Status != PlatformAdminStatus.Inactive, cancellationToken);
        return (token, admin);
    }

    private async Task AuditAsync(string action, Guid? adminId, string? ip, object? details, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "PlatformAuth",
            EntityId = adminId?.ToString() ?? "-",
            Action = action,
            UserId = adminId,
            At = clock.GetUtcNow(),
            Ip = ip,
            Changes = details is null ? "{}" : JsonSerializer.Serialize(details),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Error Invalid() => Error.Validation("auth.invalid_credentials", InvalidCredentials);
}
