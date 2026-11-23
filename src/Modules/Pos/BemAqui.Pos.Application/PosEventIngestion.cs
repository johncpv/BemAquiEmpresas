using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BemAqui.Pos.Application;

/// <summary>
/// Um evento da fila de saída do aparelho (OFF-02 CA 2): <c>OccurredAt</c> e <c>Payload</c> vão como texto, exatamente como entraram no hash. Não
/// leva dado pessoal: o documento do consumidor sobe em <see cref="PosSyncDocumentInput"/>, à parte.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PosSyncEventInput(
    Guid EventId, Guid DeviceId, Guid RegisterId, int DeviceEpoch, long Seq, int SchemaVersion, string Kind, string OccurredAt, string? PrevHash, string Hash, string Payload);

/// <summary>CPF ou CNPJ do consumidor de uma venda do lote (CA 17): vai cifrado para o <c>Sales</c>, nunca para o registro de eventos.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PosSyncDocumentInput(Guid SaleId, string Kind, string Value)
{
    public override string ToString() => $"PosSyncDocumentInput {{ SaleId = {SaleId}, Kind = {Kind} }}";
}

/// <summary>Lote de eventos do aparelho. <c>OldestPendingAt</c>: o evento mais antigo da fila do aparelho (aviso do gerente, CA 16).</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PosSyncBatch(IReadOnlyList<PosSyncEventInput> Events, IReadOnlyList<PosSyncDocumentInput>? Documents = null, DateTimeOffset? OldestPendingAt = null);

/// <summary>Como cada evento ficou (CA 5). <c>Replayed</c>: já tinha chegado antes (reenvio); a resposta é a da primeira vez.</summary>
public sealed record PosSyncEventResult(Guid EventId, int DeviceEpoch, long Seq, DeviceEventOutcome Outcome, bool Replayed);

/// <summary>Até qual <c>seq</c> da época o servidor tem tudo, sem lacuna (o aparelho reenvia a partir daí, CA 6).</summary>
public sealed record PosSyncAck(int DeviceEpoch, long Seq);

public sealed record PosSyncBatchResult(IReadOnlyList<PosSyncEventResult> Results, IReadOnlyList<PosSyncAck> Acks, DateTimeOffset ServerTime);

/// <summary>
/// Subida dos eventos do caixa (OFF-02, ingestão em dois tempos do rascunho F): cada evento é gravado no registro imutável
/// (<c>pos.device_events</c>, <c>ON CONFLICT (event_id) DO NOTHING</c>) e projetado <b>na mesma transação curta</b>, um de cada vez e na ordem do
/// <c>seq</c>, com a trava do caixa (<c>pg_advisory_xact_lock</c>): 50 reenvios em paralelo do mesmo lote gravam cada evento e cada venda uma vez,
/// e a sangria nunca corre com o fechamento da mesma sessão. A venda concluída nunca é recusada: o que não fecha vira divergência (aceita) ou
/// quarentena (o evento fica só no registro até alguém liberar, CA 15). Os efeitos nos outros módulos saem por eventos do outbox.
/// </summary>
public sealed partial class PosEventIngestion(
    IPosDb db,
    ISaleIngestion sales,
    ISaleCustomerDocuments documents,
    CashSessionIngestion cash,
    CashClosingService closing,
    PrinterService printers,
    IPosOperatorHistory operators,
    IAuditReader audit,
    IEventPublisher events,
    PosSyncAlerts alerts,
    TimeProvider clock)
{
    public const string SaleKind = "pos.sale";
    public const string CashKind = "pos.cash";
    public const string PrintKind = "pos.print";
    public const int MaxEvents = 200;
    public const int MaxPayloadLength = 256 * 1024;

    /// <summary>Rate limit da subida por aparelho (revisão de segurança M3).</summary>
    public const string RateLimitPolicy = "pos-sync-events";
    public const int RateLimitPerMinute = 120;

    /// <summary>Maior distância entre o primeiro evento da venda e a conclusão (venda em espera por um turno inteiro).</summary>
    public static readonly TimeSpan MaxSaleSpan = TimeSpan.FromHours(24);

    /// <summary>Tolerância do relógio do aparelho (ponto em aberto da story: os 2 min da ADR 0026).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    private static readonly string[] Kinds = [SaleKind, CashKind, PrintKind];

    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public async Task<Result<PosSyncBatchResult>> IngestAsync(DeviceIdentity device, PosSyncBatch batch, TimeSpan clockSkew, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(batch);
        if (Validate(batch) is { } invalid)
        {
            return invalid;
        }

        if (device.RegisterId is not { } registerId || device.StoreId is not { } storeId)
        {
            return Error.Forbidden("device_kind", "Este aparelho não é um caixa.");
        }

        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registerId && r.StoreId == storeId, cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (register is null || store is null)
        {
            return Error.Forbidden("device_replaced", PosSyncService.ReplacedMessage);
        }

        var context = new SyncContext(device, store, register, clockSkew,
            (batch.Documents ?? []).GroupBy(d => d.SaleId).ToDictionary(g => g.Key, g => g.First()), trusted: false);
        var results = new List<PosSyncEventResult>();
        foreach (var input in batch.Events.OrderBy(e => e.DeviceEpoch).ThenBy(e => e.Seq))
        {
            results.Add(await ProcessAsync(context, input, cancellationToken));
        }

        // M3: o aparelho revogado ou substituído não dispara o aviso (o OldestPendingAt dele é ignorado).
        if (!context.Replaced)
        {
            await alerts.OnContactAsync(store, register, batch.OldestPendingAt, cancellationToken);
        }
        var acks = new List<PosSyncAck>();
        foreach (var epoch in results.Select(r => r.DeviceEpoch).Distinct())
        {
            acks.Add(new PosSyncAck(epoch, await ContiguousSeqAsync(register.Id, epoch, cancellationToken)));
        }

        return new PosSyncBatchResult(results, acks, clock.GetUtcNow());
    }

    /// <summary>
    /// Liberar da quarentena (CA 15). A conferência roda de novo, sem o modo confiável: se passar, o evento entra como qualquer outro; se não passar,
    /// entra mesmo assim (quem liberou decidiu) e o que a conferência achou fica gravado como divergência visível, "Liberado apesar de: …" (revisão de
    /// segurança M4). A pendência de aparelho substituído agrupa os eventos dele: liberar libera todos, na ordem. A checagem de "já liberado" roda
    /// dentro da trava do caixa (B5).
    /// </summary>
    public async Task<Result<IReadOnlyList<SyncDivergence>>> ReleaseAsync(SyncDivergence held, Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == held.RegisterId, cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == held.StoreId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(register.Id, cancellationToken);
        if (await db.SyncDivergences.AnyAsync(d => d.EventId == held.EventId && d.Quarantined && d.Status == SyncDivergenceStatus.Resolved
                && d.Resolution == SyncResolution.ReleaseQuarantine, cancellationToken))
        {
            return Error.Conflict("already_released", "Este evento já foi liberado.");
        }

        var events = held.Kind == SyncDivergenceKind.ReplacedDevice
            ? await db.DeviceEvents.AsNoTracking().Where(e => e.DeviceId == held.DeviceId && e.RegisterId == held.RegisterId && e.DeviceRevoked && e.Outcome == DeviceEventOutcome.Quarantined)
                .OrderBy(e => e.DeviceEpoch).ThenBy(e => e.Seq).ToListAsync(cancellationToken)
            : await db.DeviceEvents.AsNoTracking().Where(e => e.EventId == held.EventId).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var added = new List<SyncDivergence>();
        foreach (var stored in events)
        {
            var device = new DeviceIdentity(stored.DeviceId, "Register", store.Id, register.Id, stored.DeviceEpoch, Revoked: false);
            var skew = TimeSpan.FromMilliseconds(stored.ClockSkewMs);
            var empty = new Dictionary<Guid, PosSyncDocumentInput>();
            await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
            Projection projection;
            string? finding;
            try
            {
                projection = await ProjectAsync(new SyncContext(device, store, register, skew, empty, trusted: false), stored.EventId, stored.Kind, stored.Payload, stored.OccurredAt, cancellationToken);
                finding = projection.Quarantine?.Summary;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                projection = new Projection();
                finding = ex is ProjectionFailedException ? ex.Message : "o evento não pôde ser conferido.";
            }

            if (finding is not null)
            {
                await RollbackAsync(transaction, cancellationToken);
                try
                {
                    projection = await ProjectAsync(new SyncContext(device, store, register, skew, empty, trusted: true), stored.EventId, stored.Kind, stored.Payload, stored.OccurredAt, cancellationToken);
                }
                catch (ProjectionFailedException failed)
                {
                    return Error.Validation("release_failed", "Não foi possível liberar: " + failed.Message);
                }

                if (projection.Quarantine is { } still)
                {
                    return Error.Validation("release_failed", "Não foi possível liberar: " + still.Summary);
                }

                projection.Divergences.Add(new Finding(SyncDivergenceKind.Validation, "Liberado da quarentena apesar de: " + finding));
            }

            var context = new SyncContext(device, store, register, skew, empty, trusted: true);
            var rows = projection.Divergences.Select(d => NewDivergence(context, stored.EventId, stored.Kind, stored.DeviceEpoch, stored.Seq, stored.OccurredAt, projection, d, quarantined: false, now)).ToList();
            db.SyncDivergences.AddRange(rows);
            added.AddRange(rows);
            AddExceptions(context, stored.EventId, stored.OccurredAt, projection, now);
        }

        await beforeCommit(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    private const string Savepoint = "projection";

    private async Task<PosSyncEventResult> ProcessAsync(SyncContext context, PosSyncEventInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(context.Register.Id, cancellationToken);
        var existing = await db.DeviceEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == input.EventId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PosSyncEventResult(existing.EventId, existing.DeviceEpoch, existing.Seq, existing.Outcome, Replayed: true);
        }

        var now = clock.GetUtcNow();
        var occurredAt = DateTimeOffset.Parse(input.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var claimOk = input.DeviceId == context.Device.DeviceId && input.RegisterId == context.Register.Id && input.DeviceEpoch == context.Device.Epoch;
        var epoch = claimOk ? input.DeviceEpoch : context.Device.Epoch;
        var hashOk = string.Equals(PosEventChain.Hash(input.EventId, input.DeviceId, input.RegisterId, input.DeviceEpoch, input.Seq, input.SchemaVersion, input.Kind,
            input.OccurredAt, input.PrevHash, input.Payload), input.Hash, StringComparison.Ordinal);
        var chainOk = claimOk && hashOk && await ChainAsync(context.Register.Id, epoch, input, cancellationToken);
        var (payload, redacted) = PosText.RedactCardNumbers(input.Payload);
        var clockOff = context.ClockOff || occurredAt > now + ClockTolerance;

        var hold = context.Replaced
            ? new Finding(SyncDivergenceKind.ReplacedDevice, "Eventos de um aparelho revogado ou substituído: confira com o operador antes de liberar (liberar libera todos os eventos dele).")
            : !context.Register.IsActive || !context.Store.IsActive ? new Finding(SyncDivergenceKind.Validation, "Evento de um caixa ou loja desativados.")
            : !claimOk ? new Finding(SyncDivergenceKind.Validation, "O evento diz ser de outro aparelho, caixa ou época.")
            : !chainOk ? new Finding(SyncDivergenceKind.BrokenSequence, hashOk ? $"Sequência quebrada no evento {input.Seq}: falta evento antes dele ou o anterior está em quarentena." : $"O hash do evento {input.Seq} não fecha.")
            : !PosEventChain.IsSupported(input.SchemaVersion) ? new Finding(SyncDivergenceKind.SchemaVersion, $"Versão {input.SchemaVersion} do evento fora do prazo de suporte.")
            : !Kinds.Contains(input.Kind) ? new Finding(SyncDivergenceKind.Validation, $"Tipo de evento desconhecido: {input.Kind}.")
            : redacted ? new Finding(SyncDivergenceKind.Validation, "O evento tinha texto que parece número de cartão: o texto foi apagado antes de gravar.")
            : await TimeOrderAsync(context.Register.Id, epoch, input.Seq, occurredAt, cancellationToken);

        Projection projection;
        if (hold is not null)
        {
            projection = Projection.Held(hold, payload, input.Kind);
        }
        else
        {
            await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
            try
            {
                projection = await ProjectAsync(context, input.EventId, input.Kind, payload, occurredAt, cancellationToken);
                if (projection.Quarantine is not null)
                {
                    await RollbackAsync(transaction, cancellationToken);
                }
            }
            catch (ProjectionFailedException failed)
            {
                await RollbackAsync(transaction, cancellationToken);
                projection = Projection.Held(new Finding(failed.Kind, failed.Message), payload, input.Kind);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // B2: erro inesperado leva o evento à quarentena (guardado, sem efeito) em vez de travar a fila do aparelho com 500.
                await RollbackAsync(transaction, cancellationToken);
                projection = Projection.Held(new Finding(SyncDivergenceKind.Validation, $"O evento não pôde ser processado ({ex.GetType().Name}); fica guardado para revisão."), payload, input.Kind);
            }
        }

        if (clockOff)
        {
            projection.Divergences.Add(new Finding(SyncDivergenceKind.DeviceClock,
                $"Relógio do aparelho {Minutes(context.ClockSkew)} do servidor: o evento diz {Local(occurredAt)}, recebido {Local(now)}.",
                new { deviceAt = occurredAt, receivedAt = now, skewMs = (long)context.ClockSkew.TotalMilliseconds }));
        }

        var outcome = projection.Quarantine is not null ? DeviceEventOutcome.Quarantined
            : projection.Divergences.Count > 0 ? DeviceEventOutcome.AcceptedWithDivergence
            : DeviceEventOutcome.Accepted;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pos.device_events (event_id, device_id, store_id, register_id, device_epoch, seq, schema_version, kind, occurred_at, received_at, clock_skew_ms,
                prev_hash, hash, chain_ok, payload, outcome, device_revoked)
            VALUES ({input.EventId}, {context.Device.DeviceId}, {context.Store.Id}, {context.Register.Id}, {epoch}, {input.Seq}, {input.SchemaVersion}, {Cut(input.Kind, 40)},
                {occurredAt}, {now}, {(long)context.ClockSkew.TotalMilliseconds}, {input.PrevHash}, {input.Hash}, {chainOk}, {payload}, {outcome.ToString()}, {context.Replaced})
            ON CONFLICT (event_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            // Outra entrega gravou primeiro (não acontece com a trava do caixa, mas o registro manda): desfaz e devolve o que ficou.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var winner = await db.DeviceEvents.AsNoTracking().FirstAsync(e => e.EventId == input.EventId, cancellationToken);
            return new PosSyncEventResult(winner.EventId, winner.DeviceEpoch, winner.Seq, winner.Outcome, Replayed: true);
        }

        if (projection.Quarantine is { } quarantine)
        {
            // M3: o aparelho revogado tem uma pendência só (a primeira ainda aberta agrupa os eventos dele), não uma por evento.
            var grouped = context.Replaced && await db.SyncDivergences.AnyAsync(
                d => d.DeviceId == context.Device.DeviceId && d.Kind == SyncDivergenceKind.ReplacedDevice && d.Status == SyncDivergenceStatus.Open, cancellationToken);
            if (!grouped)
            {
                db.SyncDivergences.Add(NewDivergence(context, input.EventId, input.Kind, epoch, input.Seq, occurredAt, projection, quarantine, quarantined: true, now));
            }

            // M3: a exceção por operador só leva o operador conferido (com sessão por PIN neste aparelho no instante).
            var verified = projection.OperatorId is { } op && await context.HadSessionAsync(operators, op, occurredAt, cancellationToken) ? op : (Guid?)null;
            db.OperatorIncidents.Add(new OperatorIncident
            {
                StoreId = context.Store.Id, RegisterId = context.Register.Id, OperatorId = verified, Kind = OperatorIncidentKind.EventRejected, EventId = input.EventId,
                SaleId = projection.SaleId, SaleNumber = projection.SaleNumber, OccurredAt = occurredAt, CreatedAt = now,
            });
        }
        else
        {
            AddExceptions(context, input.EventId, occurredAt, projection, now);
        }

        foreach (var divergence in projection.Divergences)
        {
            db.SyncDivergences.Add(NewDivergence(context, input.EventId, input.Kind, epoch, input.Seq, occurredAt, projection, divergence, quarantined: false, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PosSyncEventResult(input.EventId, epoch, input.Seq, outcome, Replayed: false);
    }

    /// <summary>
    /// M1: o instante não volta no tempo. O evento não pode ser anterior (além da tolerância de relógio) ao último evento aceito antes dele na mesma
    /// época do caixa; se for, fica na quarentena "Relógio do aparelho".
    /// </summary>
    private async Task<Finding?> TimeOrderAsync(Guid registerId, int epoch, long seq, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var previous = await db.DeviceEvents.AsNoTracking()
            .Where(e => e.RegisterId == registerId && e.DeviceEpoch == epoch && e.Seq < seq && e.Outcome != DeviceEventOutcome.Quarantined)
            .OrderByDescending(e => e.Seq).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(cancellationToken);
        return previous is { } at && occurredAt < at - ClockTolerance
            ? new Finding(SyncDivergenceKind.DeviceClock, $"O evento diz {Local(occurredAt)}, antes do evento aceito anterior ({Local(at)}): o relógio do aparelho voltou no tempo.")
            : null;
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// A sequência fecha (CA 11): o primeiro da época vem sem anterior; os outros têm o anterior gravado (seq − 1), com o mesmo hash, e ele não está
    /// numa quarentena de sequência ainda aberta (a quarentena vale "a partir dali" até alguém resolver). Nenhum outro evento com o mesmo seq.
    /// </summary>
    private async Task<bool> ChainAsync(Guid registerId, int epoch, PosSyncEventInput input, CancellationToken cancellationToken)
    {
        if (await db.DeviceEvents.AnyAsync(e => e.RegisterId == registerId && e.DeviceEpoch == epoch && e.Seq == input.Seq, cancellationToken))
        {
            return false;
        }

        if (input.Seq == 1)
        {
            return string.IsNullOrEmpty(input.PrevHash);
        }

        var previous = await db.DeviceEvents.AsNoTracking().Where(e => e.RegisterId == registerId && e.DeviceEpoch == epoch && e.Seq == input.Seq - 1)
            .Select(e => new { e.EventId, e.Hash, e.ChainOk }).FirstOrDefaultAsync(cancellationToken);
        if (previous is null || !string.Equals(previous.Hash, input.PrevHash, StringComparison.Ordinal))
        {
            return false;
        }

        return previous.ChainOk || !await db.SyncDivergences.AnyAsync(
            d => d.EventId == previous.EventId && d.Kind == SyncDivergenceKind.BrokenSequence && d.Status == SyncDivergenceStatus.Open, cancellationToken);
    }

    /// <summary>Maior seq da época sem lacuna desde o 1 (CA 5 e 6).</summary>
    private async Task<long> ContiguousSeqAsync(Guid registerId, int epoch, CancellationToken cancellationToken)
    {
        var seqs = await db.DeviceEvents.AsNoTracking().Where(e => e.RegisterId == registerId && e.DeviceEpoch == epoch).OrderBy(e => e.Seq).Select(e => e.Seq).ToListAsync(cancellationToken);
        long expected = 1;
        foreach (var seq in seqs)
        {
            if (seq != expected)
            {
                break;
            }

            expected++;
        }

        return expected - 1;
    }

    private async Task LockAsync(Guid registerId, CancellationToken cancellationToken)
    {
        var key = "pos.sync.register:" + registerId.ToString("N");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(cancellationToken);
    }

    private Task<Projection> ProjectAsync(SyncContext context, Guid eventId, string kind, string payload, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(payload);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Task.FromResult(Projection.Held(new Finding(SyncDivergenceKind.Validation, "O conteúdo do evento não é um JSON válido."), payload, kind));
        }

        return kind switch
        {
            SaleKind => ProjectSaleAsync(context, eventId, root, occurredAt, cancellationToken),
            CashKind => ProjectCashAsync(context, root, occurredAt, cancellationToken),
            PrintKind => ProjectPrintAsync(context, root, occurredAt, cancellationToken),
            _ => Task.FromResult(Projection.Held(new Finding(SyncDivergenceKind.Validation, $"Tipo de evento desconhecido: {kind}."), payload, kind)),
        };
    }

    private static SyncDivergence NewDivergence(SyncContext context, Guid eventId, string kind, int epoch, long seq, DateTimeOffset occurredAt, Projection projection, Finding finding,
        bool quarantined, DateTimeOffset now) =>
        new()
        {
            Kind = finding.Kind,
            Quarantined = quarantined,
            EventId = eventId,
            EventKind = Cut(kind, 40),
            StoreId = context.Store.Id,
            RegisterId = context.Register.Id,
            DeviceId = context.Device.DeviceId,
            DeviceEpoch = epoch,
            Seq = seq,
            SaleId = projection.SaleId,
            SaleNumber = projection.SaleNumber is { } number ? Cut(number, 40) : null,
            OperatorId = projection.OperatorId,
            Summary = Cut(finding.Summary, SyncDivergence.MaxSummaryLength),
            Details = finding.Details is null ? null : JsonSerializer.Serialize(finding.Details, DetailsJson),
            OccurredAt = occurredAt,
            CreatedAt = now,
        };

    private void AddExceptions(SyncContext context, Guid eventId, DateTimeOffset occurredAt, Projection projection, DateTimeOffset now)
    {
        foreach (var e in projection.Exceptions)
        {
            db.OperatorIncidents.Add(new OperatorIncident
            {
                StoreId = context.Store.Id, RegisterId = context.Register.Id, OperatorId = e.OperatorId, Kind = e.Kind, EventId = eventId, SaleId = projection.SaleId,
                SaleNumber = projection.SaleNumber, AuthorizedBy = e.AuthorizedBy, OccurredAt = e.At == default ? occurredAt : e.At, CreatedAt = now,
            });
        }
    }

    private static Error? Validate(PosSyncBatch batch)
    {
        if (batch.Events is null || batch.Events.Count is 0 or > MaxEvents)
        {
            return Field("events", $"Envie de 1 a {MaxEvents} eventos por lote.");
        }

        if (batch.Events.Select(e => e.EventId).Distinct().Count() != batch.Events.Count || (batch.Documents?.Count ?? 0) > MaxEvents)
        {
            return Field("events", "Evento repetido no lote, ou documentos demais.");
        }

        foreach (var e in batch.Events)
        {
            if (e.EventId == Guid.Empty || e.DeviceId == Guid.Empty || e.Seq < 1 || string.IsNullOrWhiteSpace(e.Kind) || e.Kind.Length > 40 || !PosEventChain.IsHash(e.Hash)
                || (!string.IsNullOrEmpty(e.PrevHash) && !PosEventChain.IsHash(e.PrevHash)) || e.Payload is null || e.Payload.Length > MaxPayloadLength
                || e.OccurredAt is null || e.OccurredAt.Length > 40
                || !DateTimeOffset.TryParse(e.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                return Field("events", $"Evento {e.EventId} incompleto: id, aparelho, seq, tipo, instante, hash e conteúdo são obrigatórios.");
            }
        }

        return null;
    }

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    internal static string Local(DateTimeOffset at) => BusinessTime.ToBusiness(at).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    private static string Minutes(TimeSpan skew)
    {
        var minutes = (int)Math.Round(Math.Abs(skew.TotalMinutes));
        return skew >= TimeSpan.Zero ? $"{minutes} min adiantado em relação" : $"{minutes} min atrasado em relação";
    }

    /// <summary>O que o evento é e o que ele precisa no servidor, por lote.</summary>
    internal sealed class SyncContext(DeviceIdentity device, Store store, Register register, TimeSpan clockSkew, IReadOnlyDictionary<Guid, PosSyncDocumentInput> documents, bool trusted)
    {
        private readonly Dictionary<(Guid, Guid, long), bool> _sessions = [];
        private readonly Dictionary<(Guid, string, long), bool> _permissions = [];
        private IReadOnlyDictionary<Guid, PosPaymentMethodPayload>? _methods;
        private IReadOnlyDictionary<Guid, PosItemPayload>? _items;

        public DeviceIdentity Device { get; } = device;

        public Store Store { get; } = store;

        public Register Register { get; } = register;

        public TimeSpan ClockSkew { get; } = clockSkew;

        public bool ClockOff => ClockSkew.Duration() > ClockTolerance;

        /// <summary>Aparelho revogado ou que não é mais o do caixa (outra época).</summary>
        public bool Replaced => Device.Revoked || Register.ActiveDeviceId != Device.DeviceId || Register.DeviceEpoch != Device.Epoch;

        public IReadOnlyDictionary<Guid, PosSyncDocumentInput> Documents { get; } = documents;

        /// <summary>Liberação da quarentena: quem liberou decidiu; a conferência de operador, permissão e autorização não roda de novo.</summary>
        public bool Trusted { get; } = trusted;

        public async Task<bool> HadSessionAsync(IPosOperatorHistory history, Guid userId, DateTimeOffset at, CancellationToken ct)
        {
            var key = (userId, Device.DeviceId, at.ToUnixTimeSeconds() / 60);
            if (!_sessions.TryGetValue(key, out var ok))
            {
                ok = await history.HadPinSessionAsync(userId, Device.DeviceId, at, ct);
                _sessions[key] = ok;
            }

            return ok;
        }

        public async Task<bool> HadPermissionAsync(IPosOperatorHistory history, Guid userId, string permission, DateTimeOffset at, CancellationToken ct)
        {
            var key = (userId, permission, at.ToUnixTimeSeconds() / 60);
            if (!_permissions.TryGetValue(key, out var ok))
            {
                ok = await history.HadPermissionAsync(userId, permission, at, ct);
                _permissions[key] = ok;
            }

            return ok;
        }

        public async Task<IReadOnlyDictionary<Guid, PosPaymentMethodPayload>> MethodsAsync(IPosDb db, CancellationToken ct) =>
            _methods ??= (await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == Store.Id && i.Kind == CatalogItemKind.PaymentMethod && !i.Deleted)
                .Select(i => i.Payload).ToListAsync(ct))
                .Select(p => JsonSerializer.Deserialize<PosPaymentMethodPayload>(p, PosCatalogProjector.Json)!).ToDictionary(m => m.Id);

        public async Task<IReadOnlyDictionary<Guid, PosItemPayload>> ItemsAsync(IPosDb db, IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
            _items = (await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == Store.Id && i.Kind == CatalogItemKind.Product && !i.Deleted && productIds.Contains(i.EntityId))
                .Select(i => i.Payload).ToListAsync(ct))
                .Select(p => JsonSerializer.Deserialize<PosItemPayload>(p, PosCatalogProjector.Json)!).ToDictionary(m => m.ProductId);
    }

    /// <summary>Uma divergência achada: tipo, o que divergiu em português e os valores (jsonb).</summary>
    internal sealed record Finding(SyncDivergenceKind Kind, string Summary, object? Details = null);

    /// <summary>O resultado da projeção: quarentena (nada foi gravado nos outros módulos) ou aceito, com as divergências e as exceções.</summary>
    internal sealed class Projection
    {
        public Finding? Quarantine { get; set; }

        public List<Finding> Divergences { get; } = [];

        public List<ReplayIncident> Exceptions { get; } = [];

        public Guid? SaleId { get; set; }

        public string? SaleNumber { get; set; }

        public Guid? OperatorId { get; set; }

        /// <summary>Evento segurado antes da projeção: tira do conteúdo só a venda, o número e o operador, para a tela.</summary>
        public static Projection Held(Finding finding, string payload, string kind)
        {
            var projection = new Projection { Quarantine = finding };
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                projection.SaleId = kind == SaleKind ? PosSaleReplay.GuidOf(root, "saleId") : PosSaleReplay.GuidOf(root, "saleId");
                projection.SaleNumber = PosSaleReplay.StringOf(root, kind == PrintKind ? "saleNumber" : "number");
                projection.OperatorId = PosSaleReplay.GuidOf(root, "operatorId") ?? LastOperator(root);
            }
            catch (JsonException)
            {
                // conteúdo ilegível: a pendência mostra só o evento
            }

            return projection;
        }

        private static Guid? LastOperator(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("events", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(e => e.TryGetProperty("data", out var d) ? PosSaleReplay.GuidOf(d, "operatorId") : null).LastOrDefault(id => id is not null)
                : null;
    }

    /// <summary>A projeção falhou depois de começar a gravar: a transação volta ao ponto de salvamento e o evento fica na quarentena.</summary>
    internal sealed class ProjectionFailedException(SyncDivergenceKind kind, string message) : Exception(message)
    {
        public SyncDivergenceKind Kind { get; } = kind;
    }
}
