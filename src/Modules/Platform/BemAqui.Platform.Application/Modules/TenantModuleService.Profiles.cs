using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Modules;

/// <summary>Perfil ativo do tenant, como a plataforma mostra (a aba "Perfis" é da PERF-02).</summary>
public sealed record TenantProfileView(string Code, string Name, string? Segment, DateTimeOffset AppliedAt, Guid? AppliedBy);

/// <summary>
/// Perfis de negócio do tenant (PERF-01, ADR 0046). Aplicar e remover são idempotentes, auditados e recalculam a liberação herdada
/// pelo <see cref="ApplyPlanAsync"/>, que roda depois do plano e nunca toca exceção manual. Remover só oculta: nenhum dado do tenant
/// é apagado. Os endpoints da plataforma vêm na PERF-02.
/// </summary>
public sealed partial class TenantModuleService
{
    public async Task<IReadOnlyList<TenantProfileView>> GetProfilesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await db.TenantProfiles.AsNoTracking().Where(p => p.TenantId == tenantId && p.RemovedAt == null).OrderBy(p => p.AppliedAt).ToListAsync(cancellationToken))
            .Select(p => new TenantProfileView(p.ProfileCode, BusinessProfileCatalog.Find(p.ProfileCode)?.Name ?? p.ProfileCode, p.Segment, p.AppliedAt, p.AppliedBy))
            .ToList();

    /// <summary>Aplica o perfil (e o segmento, se houver). Já aplicado: não faz nada. <paramref name="adminId"/> nulo é o sistema.</summary>
    public async Task<Result> ApplyProfileAsync(Guid tenantId, string profileCode, string? segment, Guid? adminId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileCode);
        if (!BusinessProfileCatalog.IsKnown(profileCode, segment))
        {
            return Error.Validation(Error.Codes.Validation, "Perfil de negócio ou segmento desconhecido: " + profileCode + (segment is null ? string.Empty : "/" + segment) + ".");
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa não encontrada.");
        }

        if (!await db.TenantProfiles.AnyAsync(p => p.TenantId == tenantId && p.ProfileCode == profileCode && p.Segment == segment && p.RemovedAt == null, cancellationToken))
        {
            db.TenantProfiles.Add(new TenantProfile { TenantId = tenantId, ProfileCode = profileCode, Segment = segment, AppliedAt = clock.GetUtcNow(), AppliedBy = adminId });
            AuditProfile(tenantId, profileCode, segment, "ProfileApplied", adminId);
            await db.SaveChangesAsync(cancellationToken);
        }

        await ApplyPlanAsync(tenantId, tenant.PlanId, cancellationToken);
        return Result.Success();
    }

    /// <summary>Remove o perfil: os módulos que só ele trazia ficam ocultos (a exceção manual continua valendo). Nunca apaga dado.</summary>
    public async Task<Result> RemoveProfileAsync(Guid tenantId, string profileCode, string? segment, Guid? adminId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa não encontrada.");
        }

        var active = await db.TenantProfiles.Where(p => p.TenantId == tenantId && p.RemovedAt == null).ToListAsync(cancellationToken);
        var row = active.FirstOrDefault(p => p.ProfileCode == profileCode && p.Segment == segment);
        if (row is null)
        {
            return Result.Success();
        }

        // Sem perfil nenhum o tenant voltaria a "sem filtro" (ADR 0046 §3): o último perfil não sai, troca-se aplicando outro antes.
        if (active.Count == 1)
        {
            return Error.Conflict(Error.Codes.Conflict, "A empresa precisa de pelo menos um perfil de negócio. Aplique outro perfil antes de remover este.");
        }

        row.RemovedAt = clock.GetUtcNow();
        row.RemovedBy = adminId;
        AuditProfile(tenantId, profileCode, segment, "ProfileRemoved", adminId);
        await db.SaveChangesAsync(cancellationToken);
        await ApplyPlanAsync(tenantId, tenant.PlanId, cancellationToken);
        return Result.Success();
    }

    private void AuditProfile(Guid tenantId, string profileCode, string? segment, string action, Guid? adminId) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "TenantProfile",
            EntityId = tenantId + ":" + profileCode + (segment is null ? string.Empty : "/" + segment),
            Action = action,
            UserId = adminId,
            At = clock.GetUtcNow(),
            Origin = adminId is null ? "Sistema · perfil padrão do provisionamento (ADR 0046)" : null,
            Changes = JsonSerializer.Serialize(new { profile = profileCode, segment }),
        });
}
