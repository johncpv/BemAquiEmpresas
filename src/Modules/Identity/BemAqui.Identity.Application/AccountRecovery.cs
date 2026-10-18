using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Esqueci minha senha e redefinição (FND-04 CA 12 a 15).</summary>
public sealed class AccountRecovery(
    IIdentityDb db,
    IPasswordHasher<User> hasher,
    IEmailSender email,
    TenantLinks links,
    AuthService auth,
    ISecurityStampCache stamps,
    ITenantContext tenant,
    TimeProvider clock)
{
    public const string ForgotMessage = "Se o e-mail estiver cadastrado, você receberá um link.";
    public const string LinkExpired = "Link expirado ou já usado. Peça um novo.";

    /// <summary>Sempre a mesma resposta (não revela se o e-mail existe); só o usuário ativo recebe o link.</summary>
    public async Task<string> ForgotAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var normalized = User.Normalize(emailAddress ?? string.Empty);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.Status == UserStatus.Active, cancellationToken);
        if (user is null)
        {
            return ForgotMessage;
        }

        var now = clock.GetUtcNow();
        var token = OpaqueToken.New();
        db.PasswordResets.Add(new PasswordReset { UserId = user.Id, TokenHash = OpaqueToken.Hash(token), ExpiresAt = now + PasswordReset.Lifetime, CreatedAt = now });
        email.Enqueue("password-reset", user.Email, new Dictionary<string, object?>
        {
            ["link"] = links.Absolute("/redefinir-senha?token=" + Uri.EscapeDataString(token)),
            ["expires_at"] = now + PasswordReset.Lifetime,
        }, "User", user.Id);
        await db.SaveChangesAsync(cancellationToken);
        return ForgotMessage;
    }

    /// <summary>Uso único, 1 h; revoga todas as sessões e troca o stamp; o 2FA continua ativo.</summary>
    public async Task<Result> ResetAsync(string token, string newPassword, string? ip, CancellationToken cancellationToken = default)
    {
        var hash = OpaqueToken.Hash(token ?? string.Empty);
        var now = clock.GetUtcNow();
        var reset = await db.PasswordResets.FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        var user = reset is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == reset.UserId && u.Status == UserStatus.Active, cancellationToken);
        if (reset is null || user is null || reset.UsedAt is not null || reset.ExpiresAt <= now)
        {
            return Error.Validation("auth.link_expired", LinkExpired);
        }

        if (PasswordPolicy.Validate(newPassword, user.Email) is { } problem)
        {
            return Error.Validation("auth.weak_password", problem, new Dictionary<string, string[]> { ["password"] = [problem] });
        }

        reset.UsedAt = now;
        user.SetPassword(hasher.HashPassword(user, newPassword));
        user.RegisterSuccessfulLogin(user.LastLoginAt ?? now); // desbloqueia a conta
        db.AuditEntries.Add(new AuditEntry { Entity = "Auth", EntityId = user.Id.ToString(), Action = "PasswordReset", UserId = user.Id, At = now, Ip = ip, Changes = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        await auth.RevokeAllAsync(user.Id, cancellationToken);
        stamps.Invalidate(tenant.Tenant?.Id ?? Guid.Empty, user.Id);
        return Result.Success();
    }
}
