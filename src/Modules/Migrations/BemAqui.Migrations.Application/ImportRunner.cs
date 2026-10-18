using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Executor da importação (MIG-01a): confere o CNPJ do snapshot com o do tenant (CA 6), segura o advisory lock do tenant
/// (CA 16), roda os carregadores na ordem, com uma transação por entidade (ou uma só, desfeita no fim, no <c>--dry-run</c>,
/// CA 14), e grava a staging e o resumo em <c>migration.import_runs</c> (CA 9 e 17).
/// </summary>
public sealed partial class ImportRunner(IMigrationDb db, IEnumerable<IDolibarrLoader> loaders, ITenantProfile profile, ITenantContext tenant, AuditOrigin origin, TimeProvider clock)
{
    /// <summary>Chave do advisory lock. Cada tenant tem banco próprio, então a chave fixa já é por tenant.</summary>
    public const long LockKey = 4_242_017_001L;

    public const string Busy = "Já existe uma importação em andamento neste tenant.";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<string> Available => loaders.OrderBy(l => l.Order).Select(l => l.Entity).ToList();

    public async Task<ImportRunResult> RunAsync(ISnapshotSource source, ImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        var unknown = request.Entities.Except(Available, StringComparer.Ordinal).ToList();
        if (request.Entities.Count == 0 || unknown.Count > 0)
        {
            return ImportRunResult.Refused("Entidade desconhecida: " + string.Join(", ", unknown.DefaultIfEmpty("(nenhuma informada)"))
                + ". Disponíveis: " + (Available.Count == 0 ? "nenhuma nesta versão" : string.Join(", ", Available)) + ".");
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Importação fora de um escopo de tenant.");
        var expected = await profile.DocumentAsync(tenantId, cancellationToken);
        var company = await source.CompanyDocumentAsync(cancellationToken);
        if (expected is null)
        {
            return ImportRunResult.Refused("O tenant não tem CNPJ no catálogo. Sem ele não dá para conferir o snapshot; nada foi gravado.");
        }

        if (company is null || BrazilianDocuments.Normalize(company) != expected)
        {
            return ImportRunResult.Refused("O CNPJ do snapshot (MAIN_INFO_SIREN) não é o do tenant. Confira se o snapshot é da empresa certa; nada foi gravado.");
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").SingleAsync(cancellationToken);
            if (!locked)
            {
                return ImportRunResult.Refused(Busy);
            }

            try
            {
                return await ExecuteAsync(source, request, cancellationToken);
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<ImportRunResult> ExecuteAsync(ISnapshotSource source, ImportRequest request, CancellationToken ct)
    {
        var selected = loaders.Where(l => request.Entities.Contains(l.Entity, StringComparer.Ordinal)).OrderBy(l => l.Order).ToList();
        var run = ImportRun.Start(request.Mode, selected.Select(l => l.Entity), request.Operator, await source.TakenAtAsync(ct), clock.GetUtcNow());
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        origin.Label = "Importação Dolibarr · operador " + request.Operator + " · execução " + run.Id;
        var session = new ImportSession(db, source, request.Mapping, run.Id, clock);
        var failure = request.Mode == ImportMode.DryRun
            ? await DryRunAsync(session, selected, ct)
            : await RealAsync(session, selected, ct);
        origin.Label = null;

        if (request.Mode == ImportMode.DryRun)
        {
            db.ImportItems.AddRange(session.Items);
        }

        var summaries = selected.Select(l => EntitySummary.From(l.Entity, session.ReadCount(l.Entity), session.Items.Where(i => i.Entity == l.Entity))).ToList();
        var broken = summaries.FirstOrDefault(s => failure is null && s.Total != s.Read);
        if (broken is not null)
        {
            failure = "A soma de " + broken.Entity + " (" + broken.Total + ") não fecha com o total lido (" + broken.Read + "): toda linha lida precisa ser registrada.";
        }

        var stored = await db.ImportRuns.SingleAsync(r => r.Id == run.Id, ct);
        stored.Finish(JsonSerializer.Serialize(summaries, Json), failure, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        var report = new ImportReport(stored.Id, stored.Mode, stored.Status, stored.Operator, stored.StartedAt, stored.FinishedAt, stored.SnapshotTakenAt, summaries, failure);
        return new ImportRunResult(failure is null ? 0 : 2, failure ?? "Importação concluída.", report, session.Items);
    }
}
