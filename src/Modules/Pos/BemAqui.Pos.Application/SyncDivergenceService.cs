using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Uma pendência na tela "Pendências do PDV" (CA 14): o tipo, a loja, o caixa, a venda, o operador e o que divergiu.</summary>
public sealed record SyncDivergenceView(
    Guid Id,
    SyncDivergenceKind Kind,
    string KindLabel,
    SyncDivergenceStatus Status,
    bool Quarantined,
    Guid StoreId,
    string StoreName,
    Guid RegisterId,
    int RegisterNumber,
    Guid? SaleId,
    string? SaleNumber,
    Guid? OperatorId,
    string? OperatorName,
    string Summary,
    string EventKind,
    long Seq,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    SyncResolution? Resolution,
    string? ResolutionReason,
    string? ResolvedByName,
    DateTimeOffset? ResolvedAt,
    uint Version);

/// <summary>A lista com o total por tipo (os chips da tela) e se passou do limite de linhas.</summary>
public sealed record SyncDivergenceList(IReadOnlyList<SyncDivergenceView> Items, IReadOnlyDictionary<SyncDivergenceKind, int> OpenByKind, int Total, bool Truncated);

public sealed record SyncDivergenceFilter(SyncDivergenceStatus? Status = SyncDivergenceStatus.Open, SyncDivergenceKind? Kind = null, Guid? StoreId = null, Guid? RegisterId = null);

/// <summary>Resolver a pendência (CA 15): a resolução, o motivo e a versão lida (concorrência).</summary>
public sealed record SyncResolveInput(SyncResolution Resolution, string Reason, uint Version);

/// <summary>Linha do relatório de exceções por operador (requisito da PDV-02).</summary>
public sealed record OperatorExceptionRow(Guid? OperatorId, string OperatorName, int ItemsRemoved, int QuantitiesReduced, int SalesDiscarded, int EventsRejected);

public sealed record OperatorExceptionReport(DateOnly From, DateOnly To, IReadOnlyList<OperatorExceptionRow> Rows);

public sealed record SyncExportFile(byte[] Content, string FileName);

/// <summary>
/// "Pendências do PDV" (OFF-02 CA 14 e 15) e o relatório de exceções por operador. Ver: <c>sales.view</c>; resolver: <c>sales.approve</c>
/// (conferidas no endpoint). Resolver é auditado (a divergência é <see cref="IAuditable"/>) e "Liberar da quarentena" projeta o evento.
/// </summary>
public sealed class SyncDivergenceService(IPosDb db, PosEventIngestion ingestion, IUserDirectory users, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxRows = 500;
    public const int MaxDays = 366;

    public static readonly IReadOnlyList<string> CsvHeader = ["Tipo", "Situação", "Loja", "Caixa", "Venda", "Operador", "O que divergiu", "Quando", "Resolução", "Motivo"];

    public static string Label(SyncDivergenceKind kind) => kind switch
    {
        SyncDivergenceKind.PriceOutdated => "Preço desatualizado",
        SyncDivergenceKind.DeviceClock => "Relógio do aparelho",
        SyncDivergenceKind.BrokenSequence => "Sequência quebrada",
        SyncDivergenceKind.ReplacedDevice => "Aparelho substituído",
        SyncDivergenceKind.SchemaVersion => "Versão do aparelho",
        SyncDivergenceKind.Validation => "Recusado na conferência",
        SyncDivergenceKind.Payment => "Pagamento",
        SyncDivergenceKind.CashSession => "Sessão de caixa",
        SyncDivergenceKind.FiscalDocument => "Documento fiscal",
        _ => kind.ToString(),
    };

    public static string Label(SyncResolution resolution) => resolution switch
    {
        SyncResolution.AcceptAsIs => "Aceitar como está",
        SyncResolution.ReleaseQuarantine => "Liberar da quarentena",
        SyncResolution.KeepOut => "Manter fora",
        _ => resolution.ToString(),
    };

    public async Task<SyncDivergenceList> ListAsync(SyncDivergenceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Filtered(filter);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(MaxRows).ToListAsync(cancellationToken);
        var openQuery = db.SyncDivergences.AsNoTracking().Where(d => d.Status == SyncDivergenceStatus.Open);
        if (filter.StoreId is { } store)
        {
            openQuery = openQuery.Where(d => d.StoreId == store);
        }

        var byKind = await openQuery.GroupBy(d => d.Kind).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return new SyncDivergenceList(await ViewsAsync(rows, cancellationToken), byKind, total, total > rows.Count);
    }

    public async Task<Result<SyncDivergenceView>> ResolveAsync(Guid id, SyncResolveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > SyncDivergence.MaxReasonLength || PosText.LooksLikeCardNumber(reason))
        {
            return Error.Validation(Error.Codes.Validation, $"Informe o motivo, com até {SyncDivergence.MaxReasonLength} caracteres.",
                new Dictionary<string, string[]> { ["reason"] = [$"Informe o motivo, com até {SyncDivergence.MaxReasonLength} caracteres."] });
        }

        var divergence = await db.SyncDivergences.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (divergence is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Pendência não encontrada.");
        }

        if (divergence.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A pendência mudou desde que você abriu. Recarregue.");
        }

        if (divergence.Status == SyncDivergenceStatus.Resolved)
        {
            return Error.Conflict("already_resolved", "Esta pendência já foi resolvida.");
        }

        if (!Enum.IsDefined(input.Resolution) || (input.Resolution is SyncResolution.ReleaseQuarantine or SyncResolution.KeepOut) != divergence.Quarantined)
        {
            return Error.Validation("invalid_resolution", divergence.Quarantined
                ? "Este evento está em quarentena: escolha \"Liberar da quarentena\" ou \"Manter fora\"."
                : "Este evento já entrou: a resolução é \"Aceitar como está\".");
        }

        var now = clock.GetUtcNow();
        if (input.Resolution == SyncResolution.ReleaseQuarantine)
        {
            var stored = await db.DeviceEvents.AsNoTracking().FirstAsync(e => e.EventId == divergence.EventId, cancellationToken);
            if (await db.SyncDivergences.AnyAsync(d => d.EventId == stored.EventId && d.Quarantined && d.Status == SyncDivergenceStatus.Resolved
                    && d.Resolution == SyncResolution.ReleaseQuarantine, cancellationToken))
            {
                return Error.Conflict("already_released", "Este evento já foi liberado.");
            }

            // O efeito e o fechamento da pendência (e das outras quarentenas do mesmo evento) na mesma transação, auditados.
            var released = await ingestion.ReleaseAsync(stored, async ct =>
            {
                foreach (var held in await db.SyncDivergences.Where(d => d.EventId == stored.EventId && d.Quarantined && d.Status == SyncDivergenceStatus.Open).ToListAsync(ct))
                {
                    held.Resolve(SyncResolution.ReleaseQuarantine, reason, currentUser.UserId, now);
                }
            }, cancellationToken);
            if (released.IsFailure)
            {
                return released.Error!;
            }

            return (await ViewsAsync([await db.SyncDivergences.AsNoTracking().FirstAsync(d => d.Id == id, cancellationToken)], cancellationToken))[0];
        }

        divergence.Resolve(input.Resolution, reason, currentUser.UserId, now);
        await db.SaveChangesAsync(cancellationToken);
        return (await ViewsAsync([divergence], cancellationToken))[0];
    }

    /// <summary>A lista filtrada em CSV seguro (separador ;, UTF-8 com BOM, sem fórmula).</summary>
    public async Task<SyncExportFile> ExportAsync(SyncDivergenceFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await Filtered(filter).OrderByDescending(d => d.CreatedAt).Take(5000).ToListAsync(cancellationToken);
        var views = await ViewsAsync(rows, cancellationToken);
        var lines = views.Select(v => (IReadOnlyList<string?>)[
            v.KindLabel, v.Status == SyncDivergenceStatus.Open ? (v.Quarantined ? "Em quarentena" : "Aberta") : "Resolvida", v.StoreName,
            v.RegisterNumber.ToString("00", CultureInfo.InvariantCulture), v.SaleNumber, v.OperatorName, v.Summary, PosEventIngestion.Local(v.OccurredAt),
            v.Resolution is { } r ? Label(r) : null, v.ResolutionReason]);
        return new SyncExportFile(SafeCsv.Bytes(SafeCsv.Text(CsvHeader, lines)), $"pendencias-do-pdv-{clock.GetUtcNow():yyyy-MM-dd}.csv");
    }

    /// <summary>Exceções por operador no período (data do evento em Brasília): remoções, reduções, descartes e eventos recusados na conferência.</summary>
    public async Task<Result<OperatorExceptionReport>> ExceptionsAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um período de até um ano, com a data final igual ou depois da inicial.");
        }

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), BemAqui.BuildingBlocks.Time.BusinessTime.Zone.GetUtcOffset(from.ToDateTime(TimeOnly.MinValue)));
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), BemAqui.BuildingBlocks.Time.BusinessTime.Zone.GetUtcOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        var query = db.OperatorIncidents.AsNoTracking().Where(e => e.OccurredAt >= start && e.OccurredAt < end);
        if (storeId is { } store)
        {
            query = query.Where(e => e.StoreId == store);
        }

        var groups = await query.GroupBy(e => new { e.OperatorId, e.Kind }).Select(g => new { g.Key.OperatorId, g.Key.Kind, Count = g.Count() }).ToListAsync(cancellationToken);
        var rows = new List<OperatorExceptionRow>();
        foreach (var op in groups.GroupBy(g => g.OperatorId))
        {
            int Count(OperatorIncidentKind kind) => op.Where(g => g.Kind == kind).Sum(g => g.Count);
            var name = op.Key is { } id ? await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido" : "Sem operador";
            rows.Add(new OperatorExceptionRow(op.Key, name, Count(OperatorIncidentKind.ItemRemoved), Count(OperatorIncidentKind.QuantityReduced),
                Count(OperatorIncidentKind.SaleDiscarded), Count(OperatorIncidentKind.EventRejected)));
        }

        return new OperatorExceptionReport(from, to, rows.OrderBy(r => r.OperatorName, StringComparer.CurrentCulture).ToList());
    }

    private IQueryable<SyncDivergence> Filtered(SyncDivergenceFilter filter)
    {
        var query = db.SyncDivergences.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(d => d.Kind == kind);
        }

        if (filter.StoreId is { } store)
        {
            query = query.Where(d => d.StoreId == store);
        }

        if (filter.RegisterId is { } register)
        {
            query = query.Where(d => d.RegisterId == register);
        }

        return query;
    }

    private async Task<IReadOnlyList<SyncDivergenceView>> ViewsAsync(IReadOnlyList<SyncDivergence> rows, CancellationToken cancellationToken)
    {
        var storeIds = rows.Select(r => r.StoreId).Distinct().ToList();
        var registerIds = rows.Select(r => r.RegisterId).Distinct().ToList();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var registers = await db.Registers.AsNoTracking().Where(r => registerIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var id in rows.SelectMany(r => new[] { r.OperatorId, r.ResolvedBy }).Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken);
        }

        return rows.Select(d => new SyncDivergenceView(
            d.Id, d.Kind, Label(d.Kind), d.Status, d.Quarantined, d.StoreId, stores.GetValueOrDefault(d.StoreId, "Loja removida"), d.RegisterId, registers.GetValueOrDefault(d.RegisterId),
            d.SaleId, d.SaleNumber, d.OperatorId, d.OperatorId is { } op ? names.GetValueOrDefault(op) ?? "Usuário removido" : null, d.Summary, d.EventKind, d.Seq, d.OccurredAt,
            d.CreatedAt, d.Resolution, d.ResolutionReason, d.ResolvedBy is { } by ? names.GetValueOrDefault(by) : null, d.ResolvedAt, d.Version)).ToList();
    }
}
