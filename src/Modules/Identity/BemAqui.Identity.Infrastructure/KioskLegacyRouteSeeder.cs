using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Kiosk;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Troca da rota da TV para <c>/tv#token</c> (PNL-01a CA 17, revisão pré-Fases 4 a 8 §4.2): roda uma vez por tenant (chave em
/// <c>identity.seed_patches</c>), no <c>migrate</c> do deploy desta story, antes de a API subir. Toda tela que já existir nessa hora foi
/// criada com o link antigo <c>/tv/{token}</c>: é revogada, com a auditoria "Rota da TV trocada para /tv#token", e o Proprietário é
/// avisado para gerar links novos. Sem linhas (o esperado: a tabela nasce nesta story), só registra a chave.
/// </summary>
public sealed class KioskLegacyRouteSeeder : ITenantSeeder
{
    public const string Key = "kiosk-route-fragment";
    public const string AuditReason = "Rota da TV trocada para /tv#token";

    public int Order => 30;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        if (await db.SeedPatches.AnyAsync(p => p.Key == Key, cancellationToken))
        {
            return;
        }

        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var notices = tenantScope.GetRequiredService<KioskNotices>();
        var now = clock.GetUtcNow();
        var legacy = await db.KioskScreens.Where(s => s.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var screen in legacy)
        {
            screen.Revoke(null, now);
            notices.Audit(screen.Id, "KioskRevoked", new { screen = screen.Id, reason = AuditReason });
        }

        foreach (var session in await db.KioskSessions.Where(s => s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            session.Revoke(now);
        }

        if (legacy.Count > 0)
        {
            await notices.NotifyOwnersAsync(
                legacy.Count == 1 ? "A rota da TV mudou e 1 tela foi desligada. Gere um link novo em Painel da TV." : $"A rota da TV mudou e {legacy.Count} telas foram desligadas. Gere links novos em Painel da TV.",
                cancellationToken);
        }

        db.SeedPatches.Add(new SeedPatch { Key = Key, AppliedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }
}
