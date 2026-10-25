using System.Net;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Identity.Application.Kiosk;

public enum KioskOpenStatus
{
    Opened,

    /// <summary>Token desconhecido, de outro tenant, já usado ou de tela revogada: 401 com a mensagem única.</summary>
    Invalid,

    /// <summary>Token válido, mas o IP real está fora das redes permitidas: 403 e a sessão não é criada (CA 16).</summary>
    NetworkDenied,
}

/// <summary>Resultado da troca do token: o segredo vai só para o cookie; o corpo leva só o nome da tela (CA 5).</summary>
public sealed record KioskOpenResult(KioskOpenStatus Status, string? Secret = null, string? ScreenName = null, DateTimeOffset? ExpiresAt = null);

/// <summary>Resposta da troca do token: só o nome da tela, nenhum outro dado do tenant (CA 5).</summary>
public sealed record KioskSessionOpened(string ScreenName);

/// <summary>A tela como a TV vê (só leitura): nome, escopo e a empresa para "Esta tela foi desligada por {empresa}.".</summary>
public sealed record KioskScreenContext(string Name, string? Line, IReadOnlyList<string> Views, int RotationSeconds, string CompanyName, DateTimeOffset ServerNow);

/// <summary>
/// Sessão de quiosque (PNL-01a CA 5 a 8, 10, 16 e 19; ADR 0030 §1 e §2): troca o token de uso único por um cookie opaco, confere a sessão
/// a cada request (cache de 30 s, invalidado na revogação) e no hub, e grava o uso no máximo 1 vez por minuto. O token, o cookie e o
/// hash nunca vão para log nem auditoria.
/// </summary>
public sealed class KioskSessionService(
    IIdentityDb db,
    KioskNotices notices,
    KioskSessionVersions versions,
    ITenantContext tenant,
    IMemoryCache cache,
    TimeProvider clock) : IKioskSessionValidator
{
    public static readonly TimeSpan CheckCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>Aviso de reuso do link no máximo 1 vez por hora por tela (o rate limit já segura a repetição).</summary>
    public static readonly TimeSpan ReuseNoticeInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Troca o token pelo cookie (CA 5, 6 e 16): hash no banco do tenant do host, tela não revogada, token ainda não usado e rede
    /// permitida. Consome o token (pareamento de uso único) e derruba qualquer outra sessão da tela (teto de 1).
    /// </summary>
    public async Task<KioskOpenResult> OpenAsync(string? token, IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (!KioskRules.IsPlausible(token))
        {
            return new KioskOpenResult(KioskOpenStatus.Invalid);
        }

        var hash = OpaqueToken.Hash(token!);
        var screen = await db.KioskScreens.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (screen is null || screen.IsRevoked)
        {
            return new KioskOpenResult(KioskOpenStatus.Invalid);
        }

        if (screen.ConsumedAt is not null)
        {
            await ReusedAsync(screen, cancellationToken);
            return new KioskOpenResult(KioskOpenStatus.Invalid);
        }

        if (!KioskRules.IsAllowed(ip, screen.AllowedNetworks))
        {
            notices.Audit(screen.Id, "KioskNetworkDenied", new { screen = screen.Id });
            await db.SaveChangesAsync(cancellationToken);
            return new KioskOpenResult(KioskOpenStatus.NetworkDenied);
        }

        var now = clock.GetUtcNow();
        foreach (var other in await db.KioskSessions.Where(s => s.KioskId == screen.Id && s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            other.Revoke(now);
        }

        var secret = OpaqueToken.New();
        var session = KioskSession.Open(screen.Id, OpaqueToken.Hash(secret), ip?.ToString(), now);
        db.KioskSessions.Add(session);
        screen.Consume(now);
        notices.Audit(screen.Id, "KioskSessionOpened", new { screen = screen.Id, session = session.Id });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new KioskOpenResult(KioskOpenStatus.Invalid); // duas trocas ao mesmo tempo: só uma vira sessão
        }

        if (tenant.Tenant?.Id is { } tenantId)
        {
            versions.Bump(tenantId, screen.Id);
        }

        return new KioskOpenResult(KioskOpenStatus.Opened, secret, screen.Name, session.AbsoluteExpiresAt);
    }

    public async Task<KioskSessionCheck> CheckAsync(string? cookie, IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (!KioskRules.IsPlausible(cookie) || tenant.Tenant?.Id is not { } tenantId)
        {
            return KioskSessionCheck.Invalid;
        }

        var hash = OpaqueToken.Hash(cookie!);
        var key = $"kiosk-session:{tenantId:N}:{hash}";
        var now = clock.GetUtcNow();
        if (!cache.TryGetValue(key, out CachedCheck? entry) || entry is null || entry.Version != versions.Of(tenantId, entry.KioskId) || now - entry.CheckedAt >= CheckCacheTtl)
        {
            var session = await db.KioskSessions.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
            entry = session is null ? null : await LoadAsync(tenantId, session, now, cancellationToken);
            if (entry is null)
            {
                cache.Remove(key);
                return KioskSessionCheck.Invalid;
            }

            cache.Set(key, entry, CheckCacheTtl);
        }

        return await DecideAsync(entry, ip, now, cancellationToken);
    }

    public async Task<KioskSessionCheck> CheckSessionAsync(Guid sessionId, IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (tenant.Tenant?.Id is not { } tenantId)
        {
            return KioskSessionCheck.Invalid;
        }

        var now = clock.GetUtcNow();
        var session = await db.KioskSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        var entry = session is null ? null : await LoadAsync(tenantId, session, now, cancellationToken);
        return entry is null ? KioskSessionCheck.Invalid : await DecideAsync(entry, ip, now, cancellationToken);
    }

    /// <summary>A tela da sessão para a TV (GET /api/v1/kiosk/screen), com o escopo do servidor.</summary>
    public KioskScreenContext Context(KioskSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new KioskScreenContext(session.Name, session.Line, session.Views, session.RotationSeconds, notices.CompanyName, clock.GetUtcNow());
    }

    private async Task<CachedCheck?> LoadAsync(Guid tenantId, KioskSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var version = versions.Of(tenantId, session.KioskId);
        var screen = await db.KioskScreens.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.KioskId, cancellationToken);
        if (screen is null)
        {
            return null;
        }

        var info = new KioskSessionInfo(screen.Id, session.Id, screen.Name, screen.Line, screen.Views, screen.RotationSeconds);
        var status = screen.IsRevoked ? KioskSessionStatus.Revoked : session.IsActive(now) ? KioskSessionStatus.Valid : KioskSessionStatus.Invalid;
        return new CachedCheck(screen.Id, version, now, status, info, screen.AllowedNetworks, session.LastSeenAt, session.ExpiresAt, session.AbsoluteExpiresAt);
    }

    private async Task<KioskSessionCheck> DecideAsync(CachedCheck entry, IPAddress? ip, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (entry.Status == KioskSessionStatus.Revoked)
        {
            return new KioskSessionCheck(KioskSessionStatus.Revoked, null);
        }

        if (entry.Status != KioskSessionStatus.Valid || entry.ExpiresAt <= now || entry.AbsoluteExpiresAt <= now)
        {
            return KioskSessionCheck.Invalid;
        }

        if (!KioskRules.IsAllowed(ip, entry.Networks))
        {
            return new KioskSessionCheck(KioskSessionStatus.NetworkDenied, null);
        }

        // Uso gravado no máximo 1 vez por minuto (CA 19), sem passar pela auditoria: renova os 30 dias, nunca os 180.
        if (now - entry.LastSeenAt >= KioskSession.SeenGranularity)
        {
            var expires = now + KioskSession.UnusedLimit < entry.AbsoluteExpiresAt ? now + KioskSession.UnusedLimit : entry.AbsoluteExpiresAt;
            entry.LastSeenAt = now;
            entry.ExpiresAt = expires;
            var sessionId = entry.Session.SessionId;
            await db.KioskSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, expires), cancellationToken);
            await db.KioskScreens.Where(s => s.Id == entry.KioskId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }

        return new KioskSessionCheck(KioskSessionStatus.Valid, entry.Session);
    }

    /// <summary>Segunda troca do mesmo token (CA 6): auditada e o Proprietário avisado (no máximo 1 vez por hora por tela).</summary>
    private async Task ReusedAsync(KioskScreen screen, CancellationToken cancellationToken)
    {
        notices.Audit(screen.Id, "KioskLinkReused", new { screen = screen.Id });
        var key = $"kiosk-reuse:{tenant.Tenant?.Id:N}:{screen.Id:N}";
        if (!cache.TryGetValue(key, out _))
        {
            cache.Set(key, true, ReuseNoticeInterval);
            await notices.NotifyOwnersAsync($"Tentaram usar de novo o link da tela {screen.Name}.", cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed class CachedCheck(Guid kioskId, int version, DateTimeOffset checkedAt, KioskSessionStatus status, KioskSessionInfo session, string[] networks, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, DateTimeOffset absoluteExpiresAt)
    {
        public Guid KioskId { get; } = kioskId;

        public int Version { get; } = version;

        public DateTimeOffset CheckedAt { get; } = checkedAt;

        public KioskSessionStatus Status { get; } = status;

        public KioskSessionInfo Session { get; } = session;

        public string[] Networks { get; } = networks;

        public DateTimeOffset LastSeenAt { get; set; } = lastSeenAt;

        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;

        public DateTimeOffset AbsoluteExpiresAt { get; } = absoluteExpiresAt;
    }
}
