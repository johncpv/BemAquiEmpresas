using System.Text.Json;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Tenants;

public interface IPlatformTenantsDb
{
    DbSet<Tenant> Tenants { get; }

    DbSet<TenantDomain> TenantDomains { get; }

    DbSet<Plan> Plans { get; }

    DbSet<ProvisioningLog> ProvisioningLogs { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record TenantRow(Guid Id, string Key, string LegalName, string? TradeName, string? Document, string? PrimaryHost, string? Plan, string Status, string? SchemaVersion, DateTimeOffset CreatedAt);

public sealed record ProvisioningLogRow(string Step, string Status, string? Message, DateTimeOffset At);

public sealed record TenantDetail(TenantRow Tenant, IReadOnlyList<string> Domains, IReadOnlyList<ProvisioningLogRow> Log, IReadOnlyList<string> Steps);

/// <summary>Tela de tenants do super admin (FND-03b): lista, detalhe com o log por etapa, validação, suspender e reativar.</summary>
public sealed partial class TenantAdminService(IPlatformTenantsDb db, ITenantResolver resolver, TimeProvider clock)
{
    public async Task<PagedList<TenantRow>> ListAsync(PageRequest page, string? q, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var tenants = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = "%" + q.Trim().ToLowerInvariant() + "%";
            // ToLower() vira lower() no PostgreSQL (é expressão do EF, não roda em .NET; cultura não se aplica).
#pragma warning disable CA1304, CA1311
            tenants = tenants.Where(t => EF.Functions.Like(t.LegalName.ToLower(), term) || EF.Functions.Like((t.TradeName ?? string.Empty).ToLower(), term)
                || EF.Functions.Like(t.Document ?? string.Empty, term) || EF.Functions.Like(t.Key, term)
                || db.TenantDomains.Any(d => d.TenantId == t.Id && EF.Functions.Like(d.Host, term)));
#pragma warning restore CA1304, CA1311
        }

        tenants = page.ParseSort() switch
        {
            ("nome", true) => tenants.OrderByDescending(t => t.TradeName ?? t.LegalName),
            ("nome", false) => tenants.OrderBy(t => t.TradeName ?? t.LegalName),
            ("criacao", false) => tenants.OrderBy(t => t.CreatedAt),
            _ => tenants.OrderByDescending(t => t.CreatedAt),
        };
        return await Project(tenants).ToPagedListAsync(page, cancellationToken);
    }

    public async Task<Result<TenantDetail>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await Project(db.Tenants.AsNoTracking().Where(t => t.Key == key)).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Tenant não encontrado.");
        }

        var domains = await db.TenantDomains.Where(d => d.TenantId == row.Id).OrderByDescending(d => d.IsPrimary).Select(d => d.Host).ToListAsync(cancellationToken);
        var log = await db.ProvisioningLogs.Where(l => l.TenantId == row.Id).OrderBy(l => l.At)
            .Select(l => new ProvisioningLogRow(l.Step, l.Status.ToString(), l.Message, l.At)).ToListAsync(cancellationToken);
        return new TenantDetail(row, domains, log, ProvisioningSteps.Ordered);
    }

    public async Task<IReadOnlyList<string>> PlansAsync(CancellationToken cancellationToken = default) =>
        await db.Plans.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(cancellationToken);

    public Task<Result> SuspendAsync(string key, string reason, Guid? adminId, string? ip, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(key, adminId, ip, reason, suspend: true, cancellationToken);

    public Task<Result> ReactivateAsync(string key, Guid? adminId, string? ip, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(key, adminId, ip, null, suspend: false, cancellationToken);

    /// <summary>Auditoria da plataforma (CA 8): super admin, motivo e antes/depois.</summary>
    public async Task AuditAsync(string key, string action, Guid? adminId, string? ip, object changes, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry { Entity = "Tenant", EntityId = key, Action = action, UserId = adminId, At = clock.GetUtcNow(), Ip = ip, Changes = JsonSerializer.Serialize(changes) });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result> ChangeStatusAsync(string key, Guid? adminId, string? ip, string? reason, bool suspend, CancellationToken cancellationToken)
    {
        if (suspend && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(Error.Codes.Validation, "Informe o motivo da suspensão.", new Dictionary<string, string[]> { ["reason"] = ["Informe o motivo da suspensão."] });
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Tenant não encontrado.");
        }

        var expected = suspend ? TenantLifecycle.Active : TenantLifecycle.Suspended;
        if (tenant.Status != expected)
        {
            return Error.Conflict(Error.Codes.Conflict, suspend ? "Só um tenant ativo pode ser suspenso." : "Só um tenant suspenso pode ser reativado.");
        }

        var before = tenant.Status.ToString();
        if (suspend)
        {
            tenant.Suspend();
        }
        else
        {
            tenant.Activate();
        }

        await AuditAsync(key, suspend ? "TenantSuspended" : "TenantReactivated", adminId, ip, new { before, after = tenant.Status.ToString(), reason }, cancellationToken);
        resolver.Invalidate(); // nesta instância já; nas outras, pelo NOTIFY do catálogo (< 10 s, CA 5 e 6)
        return Result.Success();
    }

    // Projeção no fim: filtro e ordenação ficam na entidade, que o EF traduz para SQL.
    private IQueryable<TenantRow> Project(IQueryable<Tenant> tenants) =>
        tenants.Select(t => new TenantRow(
            t.Id, t.Key, t.LegalName, t.TradeName, t.Document,
            db.TenantDomains.Where(d => d.TenantId == t.Id && d.IsPrimary).Select(d => d.Host).FirstOrDefault(),
            db.Plans.Where(p => p.Id == t.PlanId).Select(p => p.Name).FirstOrDefault(),
            t.Status.ToString(), t.SchemaVersion, t.CreatedAt));
}
