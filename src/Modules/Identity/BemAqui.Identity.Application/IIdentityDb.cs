using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Dados do módulo Identity (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IIdentityDb
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserInvite> UserInvites { get; }

    DbSet<PasswordReset> PasswordResets { get; }

    DbSet<RecoveryCode> RecoveryCodes { get; }

    DbSet<TwoFactorChallenge> TwoFactorChallenges { get; }

    DbSet<TenantSetting> TenantSettings { get; }

    /// <summary>Eventos de segurança (login, bloqueio, reuso de refresh...) entram na auditoria imutável.</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
