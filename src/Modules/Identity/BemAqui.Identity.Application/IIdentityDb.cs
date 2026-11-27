using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Dados do módulo Identity (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IIdentityDb
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<ShopfloorDevice> ShopfloorDevices { get; }

    DbSet<UserPin> UserPins { get; }

    /// <summary>PIN do caixa do PDV, um por usuário (PDV-01 CA 11).</summary>
    DbSet<PosPin> PosPins { get; }

    /// <summary>Verificadores sem rede entregues a cada aparelho de caixa (OFF-03).</summary>
    DbSet<DevicePinVerifier> DevicePinVerifiers { get; }

    /// <summary>Retratos das permissões por versão (OFF-02): a permissão no instante do evento do caixa.</summary>
    DbSet<PermissionSnapshot> PermissionSnapshots { get; }

    /// <summary>Telas de TV (<c>identity.kiosk_tokens</c>, PNL-01a).</summary>
    DbSet<KioskScreen> KioskScreens { get; }

    DbSet<KioskSession> KioskSessions { get; }

    DbSet<UserInvite> UserInvites { get; }

    DbSet<PasswordReset> PasswordResets { get; }

    DbSet<RecoveryCode> RecoveryCodes { get; }

    DbSet<TwoFactorChallenge> TwoFactorChallenges { get; }

    DbSet<TenantSetting> TenantSettings { get; }

    DbSet<CompanyProfile> CompanyProfiles { get; }

    DbSet<CatalogModule> CatalogModules { get; }

    DbSet<CatalogModuleAction> CatalogModuleActions { get; }

    DbSet<Group> Groups { get; }

    DbSet<GroupModule> GroupModules { get; }

    DbSet<GroupGrant> GroupPermissions { get; }

    DbSet<UserGroup> UserGroups { get; }

    DbSet<UserPreference> UserPreferences { get; }

    DbSet<AiSettings> AiSettings { get; }

    DbSet<AiUsageLog> AiUsageLogs { get; }

    DbSet<SeedPatch> SeedPatches { get; }

    /// <summary>Pedidos do titular (<c>settings.privacy_requests</c>, LGPD-01a).</summary>
    DbSet<PrivacyRequest> PrivacyRequests { get; }

    /// <summary>Aplicativos conectados (INT-01, ADR 0053): o aplicativo, a intenção de conexão, o código, a conexão e os tokens opacos.</summary>
    DbSet<ConnectedApp> ConnectedApps { get; }

    DbSet<AppConnectIntent> AppConnectIntents { get; }

    DbSet<AppAuthorizationCode> AppAuthorizationCodes { get; }

    DbSet<AppConnection> AppConnections { get; }

    DbSet<AppToken> AppTokens { get; }

    DbSet<RetentionRule> RetentionRules { get; }

    /// <summary>Eventos de segurança (login, bloqueio, reuso de refresh...) entram na auditoria imutável.</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Transação e trava (<c>pg_advisory_xact_lock</c>) da entrada por PIN no caixa (revisão de segurança da PDV-01, M2).</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    /// <summary>Concorrência otimista (xmin) da tela de TV.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
