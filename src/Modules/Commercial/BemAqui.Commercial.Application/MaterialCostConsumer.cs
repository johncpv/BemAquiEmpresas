using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Custo de material no projeto (EST-03 CA 8, ADR 0025 §2): <see cref="StockIssued"/> e <see cref="StockReturned"/> viram uma linha
/// <c>StockIssue</c>/<c>StockReturn</c> na razão <c>commercial.project_costs</c> com a diferença para o valor novo da fonte (o movimento
/// de origem). A baixa soma ao realizado e a devolução subtrai; o estorno é a mesma fonte com o <c>seq</c> seguinte e valor novo 0.
/// Idempotente pela inbox, pela regra do <c>seq</c> e pelo índice único (corrida com a recomposição: a segunda gravação falha, a
/// mensagem volta e é ignorada). Só ids, quantidades e valores do evento; nada no Commercial lê <c>inventory.*</c>.
/// </summary>
public sealed class MaterialCostConsumer(ICommercialDb db, TimeProvider clock) : IIntegrationEventConsumer<StockIssued>, IIntegrationEventConsumer<StockReturned>
{
    public Task HandleAsync(StockIssued integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var e = integrationEvent;
        return MaterialCostLedger.RecordAsync(
            db, new ProjectStockMovement(e.MovementId, 0, StockMovementType.Issue, e.Seq, e.ProjectId, e.ProductionOrderId, e.ItemId, e.Quantity, e.PreviousAmount, e.NewAmount, e.At),
            backfill: false, clock, cancellationToken);
    }

    public Task HandleAsync(StockReturned integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var e = integrationEvent;
        return MaterialCostLedger.RecordAsync(
            db, new ProjectStockMovement(e.MovementId, 0, StockMovementType.Return, e.Seq, e.ProjectId, e.ProductionOrderId, e.ItemId, e.Quantity, e.PreviousAmount, e.NewAmount, e.At),
            backfill: false, clock, cancellationToken);
    }
}

/// <summary>Regras do material na razão (EST-03 CA 8 e 11): fonte, sinal e a gravação comum ao consumidor e à recomposição.</summary>
public static class MaterialCostLedger
{
    /// <summary>Fonte da linha: a baixa é <c>StockIssue</c>; a devolução, <c>StockReturn</c>. Produção e expedição nunca entram (ADR 0025 §1).</summary>
    public static ProjectCostSource? SourceOf(StockMovementType type) => type switch
    {
        StockMovementType.Issue => ProjectCostSource.StockIssue,
        StockMovementType.Return => ProjectCostSource.StockReturn,
        _ => null,
    };

    /// <summary>Valor da fonte no projeto: a baixa soma ao realizado; a devolução subtrai (o evento traz o valor sem sinal).</summary>
    public static decimal SignedAmount(StockMovementType type, decimal amount) => type == StockMovementType.Return ? -Math.Abs(amount) : Math.Abs(amount);

    /// <summary>
    /// Grava a diferença da fonte (o movimento de origem) com o <c>seq</c> do fato, ou nada (repetido, fora de ordem ou sem projeto).
    /// Devolve se gravou.
    /// </summary>
    public static async Task<bool> RecordAsync(ICommercialDb db, ProjectStockMovement fact, bool backfill, TimeProvider clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(clock);
        if (SourceOf(fact.Type) is not { } source || fact.ProjectId == Guid.Empty || !await db.Projects.AnyAsync(p => p.Id == fact.ProjectId, ct))
        {
            return false;
        }

        var applied = await db.ProjectCosts.AsNoTracking().Where(c => c.Source == source && c.SourceId == fact.MovementId)
            .Select(c => new AppliedCost(c.Seq, c.Amount)).ToListAsync(ct);
        if (ProjectCostLedger.Difference(applied, fact.Seq, SignedAmount(fact.Type, fact.NewAmount)) is not { } difference)
        {
            return false;
        }

        var row = new ProjectCost
        {
            ProjectId = fact.ProjectId, ProductionOrderId = fact.ProductionOrderId, CostType = CostType.Material, Source = source, SourceId = fact.MovementId, Seq = fact.Seq,
            Amount = difference, OccurredOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(fact.At).DateTime), RecordedAt = clock.GetUtcNow(), IsBackfill = backfill,
        };
        db.ProjectCosts.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (backfill && DbErrors.IsUniqueViolation(e))
        {
            // Recomposição: o consumidor ao vivo gravou a mesma versão ao mesmo tempo; fica a dele (CA 11). Ao vivo, a exceção volta à
            // inbox e a mensagem repetida é ignorada pela regra do seq.
            db.Entry(row).State = EntityState.Detached;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Recomposição das saídas e devoluções com projeto publicadas antes do consumidor (EST-03 CA 11, D3, ADR 0025 §3): lê os movimentos
/// pela fonte (<see cref="IStockMovements.ProjectMovementsAsync"/>) e grava na razão só as diferenças que faltam, com
/// <c>is_backfill = true</c> ("Calculado na implantação"). Idempotente: a regra do <c>seq</c> não grava de novo o que já está na razão
/// (inclusive o que o consumidor ao vivo gravou), e o índice único resolve a corrida com ele (a linha perdedora é ignorada). Só olha
/// movimentos lançados há mais de uma hora, para não marcar como implantação o evento que ainda está no outbox.
/// </summary>
public sealed class MaterialCostBackfill(ICommercialDb db, IStockMovements movements, TenantContextHolder tenant, TimeProvider clock)
{
    public const int BatchSize = 500;

    public static readonly TimeSpan InFlight = TimeSpan.FromHours(1);

    /// <summary>Quantas linhas foram gravadas (0 com o módulo <c>inventory</c> não liberado ou nada faltando).</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased("inventory") != true)
        {
            return 0;
        }

        var cutoff = clock.GetUtcNow() - InFlight;
        var done = 0;
        long after = 0;
        while (true)
        {
            var facts = await movements.ProjectMovementsAsync(after, BatchSize, cancellationToken);
            if (facts.Count == 0)
            {
                return done;
            }

            foreach (var fact in facts.Where(f => f.At <= cutoff))
            {
                if (await MaterialCostLedger.RecordAsync(db, fact, backfill: true, clock, cancellationToken))
                {
                    done++;
                }
            }

            after = facts.Max(f => f.PostedSeq);
        }
    }
}

public sealed record MaterialCostBackfillArgs;

/// <summary>Execução por tenant da recomposição (CA 11); argumentos só de ids (o tenant).</summary>
public sealed class MaterialCostBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<MaterialCostBackfillJob> logger)
    : TenantJob<MaterialCostBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, MaterialCostBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<MaterialCostBackfill>().RunAsync(cancellationToken);
}

/// <summary>
/// Recorrente global que enfileira a recomposição por tenant ativo: disparado na subida do processo (logo depois do deploy) e repetido
/// de madrugada (03:50 de São Paulo); sem nada faltando, termina sem gravar.
/// </summary>
public sealed class MaterialCostBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<MaterialCostBackfillJob, MaterialCostBackfillArgs>(directory, jobs)
{
    public const string Id = "commercial-material-cost-backfill";
    public const string Cron = "50 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override MaterialCostBackfillArgs ArgsFor(TenantInfo tenant) => new();
}
