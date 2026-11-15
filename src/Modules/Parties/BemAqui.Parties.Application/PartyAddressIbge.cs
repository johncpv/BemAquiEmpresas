using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Lookups;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Parties.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Parties.Application;

/// <summary>
/// Código IBGE automático do endereço (a NF-e exige): endereço com município e UF e sem o código recebe o código pelo nome do
/// município (<see cref="IIbgeLookup"/>). Vale para a tela, a API e a importação, para a nota no rascunho e para a recomposição
/// diária dos que já existiam. Sem resposta do provedor, o endereço é gravado sem o código (nunca bloqueia a gravação).
/// </summary>
public sealed class PartyAddressIbge(IPartiesDb db, IIbgeLookup lookup, AuditOrigin origin)
{
    public const int BatchSize = 200;

    /// <summary>Origem na auditoria das alterações feitas pelo sistema (sem usuário logado).</summary>
    public const string AuditLabel = "Sistema · código IBGE automático";

    public static bool Missing(PartyAddress address) =>
        string.IsNullOrWhiteSpace(address.IbgeCode) && !string.IsNullOrWhiteSpace(address.City) && address.State?.Trim().Length == 2;

    /// <summary>Preenche, antes de gravar, os endereços já carregados no contexto que estão sem o código.</summary>
    public async Task FillTrackedAsync(CancellationToken cancellationToken)
    {
        foreach (var address in db.PartyAddresses.Local.Where(Missing).ToList())
        {
            await FillAsync(address, cancellationToken);
        }
    }

    /// <summary>Endereços do terceiro sem o código (rascunho da nota, FIS-03a): resolve e grava; quantos foram preenchidos.</summary>
    public async Task<int> FillPartyAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var addresses = (await db.PartyAddresses.Where(a => a.PartyId == partyId && (a.IbgeCode == null || a.IbgeCode == "") && a.City != "" && a.State != "")
            .ToListAsync(cancellationToken)).Where(Missing).ToList();
        return await FillAndSaveAsync(addresses, cancellationToken);
    }

    /// <summary>
    /// Recomposição dos endereços que já existiam sem o código (importação do Dolibarr, API): por lotes, idempotente (só toca o que
    /// continua vazio; o que não se resolve fica para a próxima execução). Quantos foram preenchidos.
    /// </summary>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        origin.Label ??= AuditLabel;
        var done = 0;
        var skipped = new List<Guid>();
        while (true)
        {
            var batch = await db.PartyAddresses.Where(a => (a.IbgeCode == null || a.IbgeCode == "") && a.City != "" && a.State != "" && !skipped.Contains(a.Id))
                .OrderBy(a => a.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return done;
            }

            done += await FillAndSaveAsync(batch.Where(Missing).ToList(), cancellationToken);
            skipped.AddRange(batch.Where(a => string.IsNullOrWhiteSpace(a.IbgeCode)).Select(a => a.Id));
        }
    }

    private async Task<int> FillAndSaveAsync(List<PartyAddress> addresses, CancellationToken cancellationToken)
    {
        var filled = 0;
        foreach (var address in addresses)
        {
            filled += await FillAsync(address, cancellationToken) ? 1 : 0;
        }

        if (filled > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return filled;
    }

    private async Task<bool> FillAsync(PartyAddress address, CancellationToken cancellationToken)
    {
        if (await lookup.FindAsync(address.State, address.City, cancellationToken) is not { } code)
        {
            return false;
        }

        address.IbgeCode = code;
        return true;
    }
}

public sealed record PartyIbgeBackfillArgs;

/// <summary>Execução por tenant da recomposição do código IBGE dos endereços.</summary>
public sealed class PartyIbgeBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PartyIbgeBackfillJob> logger)
    : TenantJob<PartyIbgeBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, PartyIbgeBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<PartyAddressIbge>().BackfillAsync(cancellationToken);
}

/// <summary>
/// Recorrente global que enfileira a recomposição por tenant ativo: disparado na subida do processo (logo depois do deploy) e repetido
/// de madrugada (03:40 de São Paulo); quando todos os endereços têm o código, a consulta não acha nada e termina.
/// </summary>
public sealed class PartyIbgeBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<PartyIbgeBackfillJob, PartyIbgeBackfillArgs>(directory, jobs)
{
    public const string Id = "parties-ibge-backfill";
    public const string Cron = "40 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override PartyIbgeBackfillArgs ArgsFor(TenantInfo tenant) => new();
}
