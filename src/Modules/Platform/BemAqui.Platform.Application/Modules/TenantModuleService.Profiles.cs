using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Modules;

/// <summary>Perfil ativo do tenant, como a plataforma mostra.</summary>
public sealed record TenantProfileView(string Code, string Name, string? Segment, DateTimeOffset AppliedAt, Guid? AppliedBy);

/// <summary>Escolha de perfil no provisionamento ou na aba "Perfis": o código do perfil e, se houver, o segmento.</summary>
public sealed record ProfileChoice(string Code, string? Segment);

public sealed record CatalogItemView(string Code, string Name);

/// <summary>Perfil do catálogo para a tela (PERF-02): o que ele libera e os segmentos dele.</summary>
public sealed record BusinessProfileView(string Code, string Name, string Description, IReadOnlyList<CatalogItemView> Modules, IReadOnlyList<CatalogItemView> Segments);

/// <summary>Módulos de um plano, para o aviso "o plano não cobre" do provisionamento (PERF-02 CA 3).</summary>
public sealed record PlanModulesView(string Name, IReadOnlyList<string> Modules);

/// <summary>Linha da aba "Perfis": ativa (sem <c>RemovedAt</c>) ou do histórico. "Por" é o e-mail do super admin ou "Sistema".</summary>
public sealed record TenantProfileEntry(string Code, string Name, string? Segment, string? SegmentName, DateTimeOffset AppliedAt, string AppliedBy, DateTimeOffset? RemovedAt, string? RemovedBy);

/// <summary>Aba "Perfis" (PERF-02 CA 6): os perfis ativos e o histórico de aplicar e remover, que é a auditoria visível.</summary>
public sealed record TenantProfilesTab(IReadOnlyList<TenantProfileEntry> Active, IReadOnlyList<TenantProfileEntry> History);

/// <summary>Resultado de "Adicionar perfil" (PERF-02 CA 7): <c>Changed</c> falso quando a empresa já tinha o perfil.</summary>
public sealed record TenantProfileChange(bool Changed, string Message);

/// <summary>
/// Perfis de negócio do tenant (PERF-01 e PERF-02, ADR 0046). Aplicar e remover são idempotentes, auditados e recalculam a liberação
/// herdada pelo <see cref="ApplyPlanAsync"/>, que roda depois do plano e nunca toca exceção manual. Remover só oculta: nenhum dado do
/// tenant é apagado.
/// </summary>
public sealed partial class TenantModuleService
{
    private const string SystemName = "Sistema";

    public static IReadOnlyList<BusinessProfileView> BusinessProfiles() =>
        BusinessProfileCatalog.All
            .Select(p => new BusinessProfileView(p.Code, p.Name, p.Description,
                p.Modules.Select(code => new CatalogItemView(code, ModuleCatalog.All.FirstOrDefault(m => m.Code == code)?.Name ?? code)).ToList(),
                p.Segments.Select(s => new CatalogItemView(s.Code, s.Name)).ToList()))
            .ToList();

    public async Task<IReadOnlyList<PlanModulesView>> PlanModulesAsync(CancellationToken cancellationToken = default)
    {
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var modules = await db.PlanModules.AsNoTracking().Select(m => new { m.PlanId, m.ModuleCode }).ToListAsync(cancellationToken);
        return plans.Select(p => new PlanModulesView(p.Name, modules.Where(m => m.PlanId == p.Id).Select(m => m.ModuleCode).Order(StringComparer.Ordinal).ToList())).ToList();
    }

    public async Task<IReadOnlyList<TenantProfileView>> GetProfilesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await db.TenantProfiles.AsNoTracking().Where(p => p.TenantId == tenantId && p.RemovedAt == null).OrderBy(p => p.AppliedAt).ToListAsync(cancellationToken))
            .Select(p => new TenantProfileView(p.ProfileCode, BusinessProfileCatalog.Find(p.ProfileCode)?.Name ?? p.ProfileCode, p.Segment, p.AppliedAt, p.AppliedBy))
            .ToList();

    public async Task<Result<TenantProfilesTab>> GetProfileTabAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa não encontrada.");
        }

        var rows = await db.TenantProfiles.AsNoTracking().Where(p => p.TenantId == tenantId).OrderBy(p => p.AppliedAt).ToListAsync(cancellationToken);
        var adminIds = rows.SelectMany(r => new[] { r.AppliedBy, r.RemovedBy }).OfType<Guid>().Distinct().ToList();
        var admins = await db.PlatformAdmins.AsNoTracking().Where(a => adminIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Email, cancellationToken);
        string Who(Guid? id) => id is { } admin ? admins.GetValueOrDefault(admin, "Super admin") : SystemName;
        var entries = rows.Select(r =>
        {
            var profile = BusinessProfileCatalog.Find(r.ProfileCode);
            return new TenantProfileEntry(r.ProfileCode, profile?.Name ?? r.ProfileCode, r.Segment, profile?.Segments.FirstOrDefault(s => s.Code == r.Segment)?.Name,
                r.AppliedAt, Who(r.AppliedBy), r.RemovedAt, r.RemovedAt is null ? null : Who(r.RemovedBy));
        }).ToList();
        return new TenantProfilesTab(entries.Where(e => e.RemovedAt is null).ToList(), entries.OrderByDescending(e => e.RemovedAt ?? e.AppliedAt).ToList());
    }

    /// <summary>"Adicionar perfil" (PERF-02 CA 7): já aplicado não muda nada e diz isso.</summary>
    public async Task<Result<TenantProfileChange>> AddProfileAsync(Guid tenantId, string profileCode, string? segment, Guid? adminId, string? ip = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileCode);
        var exists = await db.TenantProfiles.AnyAsync(p => p.TenantId == tenantId && p.ProfileCode == profileCode && p.Segment == segment && p.RemovedAt == null, cancellationToken);
        var applied = await ApplyProfileAsync(tenantId, profileCode, segment, adminId, ip, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        var label = Label(profileCode, segment);
        return exists
            ? new TenantProfileChange(false, "Esta empresa já tem o perfil " + label + ".")
            : new TenantProfileChange(true, "Perfil " + label + " aplicado. Os módulos do perfil cobertos pelo plano já estão liberados.");
    }

    /// <summary>Aplica o perfil (e o segmento, se houver). Já aplicado: não faz nada. <paramref name="adminId"/> nulo é o sistema.</summary>
    public async Task<Result> ApplyProfileAsync(Guid tenantId, string profileCode, string? segment, Guid? adminId, string? ip = null, CancellationToken cancellationToken = default)
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
            AuditProfile(tenantId, profileCode, segment, "ProfileApplied", adminId, ip);
            await db.SaveChangesAsync(cancellationToken);
        }

        await ApplyPlanAsync(tenantId, tenant.PlanId, cancellationToken);
        return Result.Success();
    }

    /// <summary>Remove o perfil: os módulos que só ele trazia ficam ocultos (a exceção manual continua valendo). Nunca apaga dado.</summary>
    public async Task<Result> RemoveProfileAsync(Guid tenantId, string profileCode, string? segment, Guid? adminId, string? ip = null, CancellationToken cancellationToken = default)
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
        AuditProfile(tenantId, profileCode, segment, "ProfileRemoved", adminId, ip);
        await db.SaveChangesAsync(cancellationToken);
        await ApplyPlanAsync(tenantId, tenant.PlanId, cancellationToken);
        return Result.Success();
    }

    /// <summary>"Comércio · Balcão" ou só "E-commerce", para mensagens e para o log do provisionamento.</summary>
    public static string Label(string profileCode, string? segment)
    {
        var profile = BusinessProfileCatalog.Find(profileCode);
        var name = profile?.Name ?? profileCode;
        var segmentName = segment is null ? null : profile?.Segments.FirstOrDefault(s => s.Code == segment)?.Name ?? segment;
        return segmentName is null ? name : name + " · " + segmentName;
    }

    private void AuditProfile(Guid tenantId, string profileCode, string? segment, string action, Guid? adminId, string? ip) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "TenantProfile",
            EntityId = tenantId + ":" + profileCode + (segment is null ? string.Empty : "/" + segment),
            Action = action,
            UserId = adminId,
            At = clock.GetUtcNow(),
            Ip = ip,
            Origin = adminId is null ? "Sistema · perfil padrão do provisionamento (ADR 0046)" : null,
            Changes = JsonSerializer.Serialize(new { profile = profileCode, segment }),
        });
}
