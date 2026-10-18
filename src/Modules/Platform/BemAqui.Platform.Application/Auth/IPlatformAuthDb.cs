using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Auth;

/// <summary>Tabelas do login do super admin no catálogo (FND-13).</summary>
public interface IPlatformAuthDb
{
    DbSet<PlatformAdmin> PlatformAdmins { get; }

    DbSet<PlatformAdminToken> PlatformAdminTokens { get; }

    DbSet<PlatformRecoveryCode> PlatformRecoveryCodes { get; }

    DbSet<PlatformSession> PlatformSessions { get; }

    /// <summary>Login, falha, bloqueio e saída do super admin vão para a auditoria imutável do catálogo (CA 8).</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Seção <c>PlatformAuth</c> (ADR 0012).</summary>
public sealed class PlatformAuthOptions
{
    public const string Section = "PlatformAuth";

    /// <summary>Sessão sem uso por este tempo expira e exige login com 2FA de novo (CA 9).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Teto da sessão, mesmo em uso contínuo.</summary>
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan SetPasswordLinkLifetime { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed record PlatformChallenge(string Challenge, PlatformTokenPurpose Purpose, DateTimeOffset ExpiresAt);

public sealed record PlatformTotpSetup(string ManualKey, string OtpAuthUri);

public sealed record PlatformSessionIssued(string Token, DateTimeOffset IdleExpiresAt);

public sealed record PlatformTwoFactorActivated(IReadOnlyList<string> RecoveryCodes, PlatformSessionIssued Session);

public sealed record PlatformAdminInvited(Guid AdminId, string SetPasswordToken, DateTimeOffset ExpiresAt);
