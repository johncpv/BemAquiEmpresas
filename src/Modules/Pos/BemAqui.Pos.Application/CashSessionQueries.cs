using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

public sealed record CashSessionFilter(Guid? StoreId = null, Guid? RegisterId = null, Guid? OperatorId = null, CashSessionStatus? Status = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// Uma sessão na lista Loja → Caixas (CX-02). <c>TotalDifference</c> (soma das quebras) só para quem tem <c>sales.approve</c>; para os outros, nulo.
/// <c>AwaitingSales</c>: contada, esperando as vendas do aparelho subirem.
/// </summary>
public sealed record CashSessionRow(
    Guid Id, Guid StoreId, string StoreName, Guid RegisterId, int RegisterNumber, Guid OperatorId, string OperatorName, CashSessionStatus Status, string StatusLabel,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, bool AwaitingSales, decimal? TotalDifference, int ReopenCount);

public sealed record CashSessionList(IReadOnlyList<CashSessionRow> Items, int PendingReview, bool Truncated, bool ExpectedVisible);

/// <summary>Uma forma no quadro do fechamento (CA 7). Esperado, Pix não localizado e diferença só para quem tem <c>sales.approve</c>.</summary>
public sealed record CashSessionLineView(
    Guid? PaymentMethodId, string MethodName, string MethodType, decimal Counted, decimal? Expected, decimal? PixNotFound, decimal? Difference, bool? AboveTolerance);

/// <summary>Fundo de troco, sangria, suprimento, recolhimento e devolução do recolhimento. <c>AboveExpected</c> só para quem aprova.</summary>
public sealed record CashSessionMovementView(Guid Id, CashMovementKind Kind, string KindLabel, decimal Amount, string? Reason, string OperatorName, DateTimeOffset OccurredAt, bool? AboveExpected);

/// <summary>Um Pix atestado pelo operador, para conferir contra o extrato (CA 13).</summary>
public sealed record CashSessionPixView(Guid PaymentId, Guid SaleId, string SaleNumber, decimal Amount, DateTimeOffset? ConfirmedAt, string? OperatorName, PixCheckStatus? Status, string? CheckedByName);

/// <summary>As vendas da sessão (CA 7 e 9): quantidade, canceladas, as feitas sem rede; valores só para quem aprova.</summary>
public sealed record CashSessionSalesView(int Sales, int Cancelled, int Offline, decimal? Total, decimal? Discounts, decimal? CancelledTotal);

/// <summary>A sessão de caixa na retaguarda (CA 7): o quadro por forma, as vendas, os movimentos, o Pix a conferir, a revisão e a reabertura.</summary>
public sealed record CashSessionDetail(
    Guid Id, Guid StoreId, string StoreName, Guid RegisterId, int RegisterNumber, Guid OperatorId, string OperatorName, CashSessionStatus Status, string StatusLabel,
    DateTimeOffset OpenedAt, DateTimeOffset? CountedAt, string? CountedByName, DateTimeOffset? ClosedAt, bool AwaitingSales, int? DeviceSalesCount,
    decimal OpeningAmount, decimal? KeptAmount, decimal? DepositAmount, decimal Tolerance, IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<CashSessionLineView> Lines, CashSessionSalesView Sales, IReadOnlyList<CashSessionMovementView> Movements, IReadOnlyList<CashSessionPixView> Pix,
    string? ApprovedByName, DateTimeOffset? ApprovedAt, string? ApprovalReason, int ReopenCount, string? ReopenedByName, DateTimeOffset? ReopenedAt, string? ReopenReason,
    bool ExpectedVisible, bool CanApprove, bool CanReopen, uint Version, CashSessionOfflineView? Offline = null);

/// <summary>
/// OFF-03 CA 6, 8 e 13: o que aconteceu sem rede na sessão. Abertura e contagem sem rede, os números das vendas concluídas sem rede (até 200) e as
/// aprovações de supervisor sem rede (quantas e o valor que mexeram).
/// </summary>
public sealed record CashSessionOfflineView(bool OpenedOffline, bool ClosedOffline, IReadOnlyList<string> Sales, int Approvals, decimal ApprovalsAmount);

public sealed record CashSessionDecisionInput(string Reason, uint Version);

public sealed record CashPixCheckInput(Guid PaymentId, PixCheckStatus Status);

public sealed record CashSessionFile(byte[] Content, string FileName);

/// <summary>
/// Loja → Caixas (CX-02 CA 7 a 13): a lista das sessões, o quadro do fechamento, a aprovação, a reabertura, a conferência do Pix, a exportação e o
/// relatório. Ver: <c>sales.view</c>; aprovar, reabrir e conferir Pix: <c>sales.approve</c> (conferidas no endpoint). O esperado e a diferença só
/// saem para quem tem <c>sales.approve</c> (CA 3), conferido aqui pelo usuário do request, em todas as rotas.
/// </summary>
public sealed class CashSessionQueries(
    IPosDb db, CashClosingService closing, ICashSessionSales sales, BemAqui.Finance.Contracts.ICashSessionFinance finance, IEventPublisher events, INotificationService notifications, IUserDirectory users,
    ICurrentUser currentUser, IPermissionService permissions, IAuditWriter audit, TimeProvider clock)
{
    private static readonly Error ConcurrencyError = Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O caixa mudou desde que você abriu. Recarregue.");

    public const int MaxRows = 500;
    public const int MaxReasonLength = 300;
    public const string OwnSessionMessage = "Quem operou o caixa não pode aprovar o próprio fechamento.";
    public const string OwnReopenMessage = "Quem operou o caixa não pode reabrir o próprio fechamento.";
    public const string ReopenNotificationType = "pos.cash_reopened";

    public static readonly IReadOnlyList<string> CsvHeader = ["Loja", "Caixa", "Operador", "Situação", "Abertura", "Fechamento", "Quebra (R$)", "Reaberturas"];

    public const string SentLabel = "Enviado";
    public const string PixClosedMessage = "Reabra o caixa para conferir o Pix.";
    public const string OwnPixMessage = "Quem operou, contou o caixa ou atestou o Pix não pode conferi-lo.";

    /// <summary>
    /// A situação que o usuário vê (revisão de segurança, M3): sem <c>sales.approve</c>, "em conferência" e "fechado" são a mesma coisa ("Enviado"),
    /// para a situação não revelar a quebra que o operador não pode ver.
    /// </summary>
    public static (CashSessionStatus Status, string Label) Shown(CashSessionStatus status, bool expectedVisible) =>
        expectedVisible ? (status, Label(status))
        : status is CashSessionStatus.PendingReview or CashSessionStatus.Closed ? (CashSessionStatus.Closed, SentLabel)
        : (status, Label(status));

    public static string Label(CashSessionStatus status) => status switch
    {
        CashSessionStatus.Open => "Aberto",
        CashSessionStatus.Counting => "Em contagem",
        CashSessionStatus.Counted => "Contado",
        CashSessionStatus.PendingReview => "Em conferência",
        CashSessionStatus.Closed => "Fechado",
        _ => status.ToString(),
    };

    public static string Label(CashMovementKind kind) => kind switch
    {
        CashMovementKind.Opening => "Fundo de troco",
        CashMovementKind.CashOut => "Sangria",
        CashMovementKind.CashIn => "Suprimento",
        CashMovementKind.Deposit => "Recolhimento ao Cofre",
        CashMovementKind.DepositReturn => "Recolhimento devolvido (reabertura)",
        CashMovementKind.Refund => "Devolução em dinheiro",
        _ => kind.ToString(),
    };

    public static IReadOnlyList<string> Labels(CashReviewReasons reasons)
    {
        var list = new List<string>();
        if (reasons.HasFlag(CashReviewReasons.BreakAboveTolerance))
        {
            list.Add("Quebra acima da tolerância da loja");
        }

        if (reasons.HasFlag(CashReviewReasons.CashOutAboveExpected))
        {
            list.Add("Sangria acima do dinheiro calculado");
        }

        if (reasons.HasFlag(CashReviewReasons.OfflineApprovals))
        {
            list.Add("Aprovações sem rede acima do limite da loja");
        }

        return list;
    }

    /// <summary>Quem pode ver o esperado e a diferença (CA 3): <c>sales.approve</c> ou o Proprietário.</summary>
    public async Task<bool> ExpectedVisibleAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } user)
        {
            return false;
        }

        var effective = await permissions.GetAsync(user, cancellationToken);
        return effective.IsOwner || effective.Has("sales.approve");
    }

    public async Task<CashSessionList> ListAsync(CashSessionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var visible = await ExpectedVisibleAsync(cancellationToken);
        var query = Filtered(filter, visible);
        var sessions = await query.OrderByDescending(s => s.OpenedAt).Take(MaxRows + 1).ToListAsync(cancellationToken);
        var pending = visible ? await db.CashSessions.CountAsync(s => s.Status == CashSessionStatus.PendingReview, cancellationToken) : 0;
        var truncated = sessions.Count > MaxRows;
        sessions = sessions.Take(MaxRows).ToList();
        var ids = sessions.Select(s => s.Id).ToList();
        var current = sessions.Where(s => s.CountId != null).ToDictionary(s => s.Id, s => s.CountId!.Value);
        var differences = visible
            ? (await db.CashCounts.AsNoTracking().Where(c => ids.Contains(c.SessionId) && c.Difference != null)
                    .Select(c => new { c.SessionId, c.CountId, Difference = c.Difference!.Value }).ToListAsync(cancellationToken))
                .Where(c => current.GetValueOrDefault(c.SessionId) == c.CountId).GroupBy(c => c.SessionId).ToDictionary(g => g.Key, g => g.Sum(c => c.Difference))
            : new Dictionary<Guid, decimal>();
        var names = await NamesAsync(sessions.Select(s => s.OperatorId), cancellationToken);
        var places = await PlacesAsync(cancellationToken);
        var arrived = await ArrivedAsync(ids, cancellationToken);
        var rows = sessions.Select(s =>
        {
            var shown = Shown(s.Status, visible);
            return new CashSessionRow(
                s.Id, s.StoreId, places.Stores.GetValueOrDefault(s.StoreId, "Loja removida"), s.RegisterId, places.Registers.GetValueOrDefault(s.RegisterId),
                s.OperatorId, names.GetValueOrDefault(s.OperatorId, "Usuário removido"), shown.Status, shown.Label, s.OpenedAt, s.ClosedAt,
                Awaiting(s, arrived), visible ? differences.GetValueOrDefault(s.Id) : null, s.ReopenCount);
        }).ToList();
        return new CashSessionList(rows, pending, truncated, visible);
    }

    public async Task<Result<CashSessionDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return session is null ? Error.NotFound(Error.Codes.NotFound, "Sessão de caixa não encontrada.") : await DetailAsync(session, cancellationToken);
    }

    /// <summary>
    /// "Aprovar fechamento" (CA 7 e 8): a sessão em conferência (ou contada e esperando vendas que não vão chegar) vai a <c>Closed</c>, com o motivo;
    /// quem operou ou contou o caixa recebe 403.
    /// </summary>
    public async Task<Result<CashSessionDetail>> ApproveAsync(Guid id, CashSessionDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ReasonError(input.Reason) is { } invalid)
        {
            return invalid;
        }

        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Sessão de caixa não encontrada.");
        }

        if (currentUser.UserId is not { } user || user == session.OperatorId || user == session.CountedBy)
        {
            return Error.Forbidden("own_cash_session", OwnSessionMessage);
        }

        if (session.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O caixa mudou desde que você abriu. Recarregue.");
        }

        if (session.Status is not (CashSessionStatus.PendingReview or CashSessionStatus.Counted))
        {
            return Error.Conflict("cash_session_not_in_review", "Só o fechamento em conferência (ou contado) pode ser aprovado.");
        }

        var lines = await closing.RecalculateAsync(session, cancellationToken);
        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
        var now = clock.GetUtcNow();
        session.ApprovedBy = user;
        session.ApprovedAt = now;
        session.ApprovalReason = input.Reason.Trim();
        closing.Close(session, register, user, now, lines);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(session, cancellationToken);
    }

    /// <summary>
    /// "Reabrir caixa" (CA 10): só a última sessão do caixa e só <c>Closed</c>; quem operou não reabre (403). A sessão volta a <c>Open</c>, o
    /// recolhimento volta à gaveta (movimento <c>DepositReturn</c>), o <see cref="CashSessionReopened"/> vai para o outbox (a CX-03 estorna os
    /// lançamentos) e o Proprietário é avisado. Auditado (a sessão é <c>IAuditable</c>).
    /// </summary>
    public async Task<Result<CashSessionDetail>> ReopenAsync(Guid id, CashSessionDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ReasonError(input.Reason) is { } invalid)
        {
            return invalid;
        }

        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Sessão de caixa não encontrada.");
        }

        if (currentUser.UserId is not { } user || user == session.OperatorId || user == session.CountedBy)
        {
            return Error.Forbidden("own_cash_session", OwnReopenMessage);
        }

        if (session.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O caixa mudou desde que você abriu. Recarregue.");
        }

        if (session.Status != CashSessionStatus.Closed)
        {
            return Error.Conflict("cash_session_not_closed", "Só o caixa fechado pode ser reaberto.");
        }

        if (await db.CashSessions.AnyAsync(s => s.RegisterId == session.RegisterId && s.Id != session.Id && s.OpenedAt >= session.OpenedAt, cancellationToken))
        {
            return Error.Conflict("cash_session_not_last", "Só a última sessão do caixa pode ser reaberta.");
        }

        // CX-03 CA 7: título da sessão já baixado fora do fechamento (o depósito da adquirente conciliado) impede a reabertura.
        if (await finance.HasReconciledReceiptAsync(session.Id, cancellationToken))
        {
            return Error.Validation(BemAqui.Finance.Contracts.ICashSessionFinance.ReconciledCode, BemAqui.Finance.Contracts.ICashSessionFinance.ReconciledMessage);
        }

        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
        var now = clock.GetUtcNow();
        if (session.DepositAmount is { } deposit && deposit > 0)
        {
            var back = CashMovement.FromDevice(Guid.CreateVersion7(), session.Id, CashMovementKind.DepositReturn, deposit, "Reabertura do caixa", user, now, false, now);
            db.CashMovements.Add(back);
            closing.Publish(back, register);
        }

        session.Status = CashSessionStatus.Open;
        session.ReopenCount++;
        session.ReopenedBy = user;
        session.ReopenedAt = now;
        session.ReopenReason = input.Reason.Trim();
        session.ClosedAt = null;
        session.ClosedBy = null;
        session.CountStartedAt = null;
        session.CountId = null;
        session.CountedBy = null;
        session.CountedAt = null;
        session.DeviceSalesCount = null;
        session.KeptAmount = null;
        session.DepositAmount = null;
        session.DepositMovementId = null;
        session.ReviewReasons = CashReviewReasons.None;
        session.ApprovedBy = null;
        session.ApprovedAt = null;
        session.ApprovalReason = null;
        events.Publish(new CashSessionReopened(session.Id, session.StoreId, session.RegisterId, user, now, session.ReopenCount));
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == session.StoreId, cancellationToken);
        var who = await users.DisplayNameAsync(user, cancellationToken) ?? "Usuário";
        var title = string.Create(CultureInfo.InvariantCulture, $"Caixa {register.Number:00} da loja {store.Name} reaberto por {who}.");
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, ReopenNotificationType, title, CashClosingService.Link + "?sessionId=" + session.Id);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyError;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CashSessionIngestion.OpenSessionIndex))
        {
            return Error.Conflict("cash_session_open", "Este caixa já tem outra sessão aberta.");
        }

        return await DetailAsync(session, cancellationToken);
    }

    /// <summary>"Conferido" ou "Não localizado" num Pix atestado pelo operador (CA 13): o não localizado vira quebra da forma Pix.</summary>
    public async Task<Result<CashSessionDetail>> CheckPixAsync(Guid id, CashPixCheckInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Status))
        {
            return CashClosingService.Field("status", "Escolha Conferido ou Não localizado.");
        }

        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Sessão de caixa não encontrada.");
        }

        if (session.Status is CashSessionStatus.Open or CashSessionStatus.Counting)
        {
            return Error.Conflict("cash_session_not_counted", "Confira o Pix depois da contagem do caixa.");
        }

        // Revisão de segurança (M2): no caixa fechado a quebra já foi publicada; mudar o Pix exige reabrir (a CX-03 estorna e lança de novo).
        if (session.Status == CashSessionStatus.Closed)
        {
            return Error.Validation("cash_session_closed", PixClosedMessage);
        }

        // Segregação (M2): quem operou ou contou o caixa, ou atestou este Pix, não confere o Pix.
        if (currentUser.UserId is not { } user || user == session.OperatorId || user == session.CountedBy)
        {
            return Error.Forbidden("own_cash_session", OwnPixMessage);
        }

        var methods = await closing.MethodTypesAsync(session.StoreId, cancellationToken);
        var pixMethods = methods.Where(m => m.Value == CashClosing.PixType).Select(m => m.Key).ToList();
        var payment = (await sales.SummaryAsync(session.Id, pixMethods, cancellationToken)).ManualPayments.FirstOrDefault(p => p.PaymentId == input.PaymentId);
        if (payment is null)
        {
            return Error.NotFound("pix_not_found", "Este Pix não é desta sessão de caixa.");
        }

        if (user == payment.ConfirmedBy)
        {
            return Error.Forbidden("own_cash_session", OwnPixMessage);
        }

        var now = clock.GetUtcNow();
        var check = await db.CashPixChecks.FirstOrDefaultAsync(c => c.SessionId == session.Id && c.PaymentId == payment.PaymentId, cancellationToken);
        if (check is null)
        {
            db.CashPixChecks.Add(new CashPixCheck
            {
                SessionId = session.Id, PaymentId = payment.PaymentId, SaleId = payment.SaleId, PaymentMethodId = payment.PaymentMethodId, Amount = payment.Amount,
                Status = input.Status, CheckedBy = user, CheckedAt = now,
            });
        }
        else
        {
            check.Status = input.Status;
            check.CheckedBy = user;
            check.CheckedAt = now;
        }

        if (session.CountId is not null)
        {
            await closing.RecalculateAsync(session, cancellationToken);
        }

        // A conferência grava junto com a sessão (B2): a versão dela barra a corrida com Aprovar ou Reabrir.
        db.CashSessions.Entry(session).Property(s => s.Status).IsModified = true;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(session, cancellationToken);
    }

    /// <summary>A lista em CSV seguro (a quebra só para quem aprova).</summary>
    public async Task<CashSessionFile> ExportAsync(CashSessionFilter filter, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(filter, cancellationToken);
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var rows = list.Items.Select(r => (IReadOnlyList<string?>)[
            r.StoreName, r.RegisterNumber.ToString("00", pt), r.OperatorName, r.StatusLabel, BusinessTime.ToBusiness(r.OpenedAt).ToString("dd/MM/yyyy HH:mm", pt),
            r.ClosedAt is { } c ? BusinessTime.ToBusiness(c).ToString("dd/MM/yyyy HH:mm", pt) : string.Empty, r.TotalDifference?.ToString("N2", pt) ?? string.Empty,
            r.ReopenCount.ToString(pt)]);
        await audit.RecordExportAsync(nameof(CashSession), "lista", "csv", list.Items.Count, new Dictionary<string, string?>
        {
            ["storeId"] = filter.StoreId?.ToString(), ["registerId"] = filter.RegisterId?.ToString(), ["operatorId"] = filter.OperatorId?.ToString(),
            ["status"] = filter.Status?.ToString(), ["from"] = filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, cancellationToken);
        return new CashSessionFile(SafeCsv.Bytes(SafeCsv.Text(CsvHeader, rows)), $"caixas-{BusinessTime.Today(clock):yyyy-MM-dd}.csv");
    }

    internal async Task<CashSessionDetail> DetailAsync(CashSession session, CancellationToken cancellationToken)
    {
        var visible = await ExpectedVisibleAsync(cancellationToken);
        var places = await PlacesAsync(cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == session.StoreId, cancellationToken);
        var methods = await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == session.StoreId && i.Kind == CatalogItemKind.PaymentMethod)
            .Select(i => new { i.EntityId, i.Payload }).ToListAsync(cancellationToken);
        var methodNames = methods.Select(m => System.Text.Json.JsonSerializer.Deserialize<PosPaymentMethodPayload>(m.Payload, PosCatalogProjector.Json)!)
            .ToDictionary(m => m.Id, m => (m.Name, Type: m.Type.ToString()));
        var counts = session.CountId is { } countId
            ? await db.CashCounts.AsNoTracking().Where(c => c.SessionId == session.Id && c.CountId == countId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken)
            : new List<CashCount>();
        var lines = counts.Select(c => new CashSessionLineView(
            c.PaymentMethodId, c.PaymentMethodId is { } m && methodNames.TryGetValue(m, out var n) ? n.Name : "Dinheiro", c.MethodType, c.Counted,
            visible ? c.Expected : null, visible ? c.PixNotFound : null, visible ? c.Difference : null,
            visible && c.Difference is { } d ? Math.Abs(d) > store.CashBreakTolerance : null)).ToList();
        var movements = await db.CashMovements.AsNoTracking().Where(m => m.SessionId == session.Id).OrderBy(m => m.OccurredAt).ToListAsync(cancellationToken);
        var pixMethods = methodNames.Where(m => m.Value.Type == CashClosing.PixType).Select(m => m.Key).ToList();
        var summary = await sales.SummaryAsync(session.Id, pixMethods, cancellationToken);
        var checks = await db.CashPixChecks.AsNoTracking().Where(c => c.SessionId == session.Id).ToDictionaryAsync(c => c.PaymentId, cancellationToken);
        var names = await NamesAsync(
            new[] { session.OperatorId, session.CountedBy, session.ApprovedBy, session.ReopenedBy }.Concat(movements.Select(m => (Guid?)m.OperatorId))
                .Concat(summary.ManualPayments.Select(p => p.ConfirmedBy)).Concat(checks.Values.Select(c => (Guid?)c.CheckedBy)).OfType<Guid>(), cancellationToken);
        string? NameOf(Guid? id) => id is { } g ? names.GetValueOrDefault(g, "Usuário removido") : null;
        var arrived = await ArrivedAsync([session.Id], cancellationToken);
        var user = currentUser.UserId;
        var own = user is null || user == session.OperatorId || user == session.CountedBy;
        var last = !await db.CashSessions.AnyAsync(s => s.RegisterId == session.RegisterId && s.Id != session.Id && s.OpenedAt >= session.OpenedAt, cancellationToken);
        var shown = Shown(session.Status, visible);
        return new CashSessionDetail(
            session.Id, session.StoreId, store.Name, session.RegisterId, places.Registers.GetValueOrDefault(session.RegisterId), session.OperatorId, NameOf(session.OperatorId)!,
            shown.Status, shown.Label, session.OpenedAt, session.CountedAt, NameOf(session.CountedBy), session.ClosedAt, Awaiting(session, arrived), session.DeviceSalesCount,
            session.OpeningAmount, session.KeptAmount, session.DepositAmount, store.CashBreakTolerance, visible ? Labels(session.ReviewReasons) : [],
            lines,
            new CashSessionSalesView(summary.Sales, summary.Cancelled, summary.Offline, visible ? summary.Total : null, visible ? summary.Discounts : null, visible ? summary.CancelledTotal : null),
            movements.Select(m => new CashSessionMovementView(m.Id, m.Kind, Label(m.Kind), m.Amount, m.Reason, NameOf(m.OperatorId)!, m.OccurredAt,
                visible && m.Kind == CashMovementKind.CashOut ? m.AboveExpected : null)).ToList(),
            visible
                ? summary.ManualPayments.Select(p => new CashSessionPixView(p.PaymentId, p.SaleId, p.SaleNumber, p.Amount, p.ConfirmedAt, NameOf(p.ConfirmedBy),
                    checks.TryGetValue(p.PaymentId, out var c) ? c.Status : null, checks.TryGetValue(p.PaymentId, out var by) ? NameOf(by.CheckedBy) : null)).ToList()
                : [],
            // A aprovação revela que houve conferência: só para quem aprova (M3).
            visible ? NameOf(session.ApprovedBy) : null, visible ? session.ApprovedAt : null, visible ? session.ApprovalReason : null, session.ReopenCount, NameOf(session.ReopenedBy), session.ReopenedAt, session.ReopenReason,
            visible,
            visible && !own && session.Status is CashSessionStatus.PendingReview or CashSessionStatus.Counted,
            visible && !own && session.Status == CashSessionStatus.Closed && last,
            session.Version,
            await OfflineAsync(session, cancellationToken));
    }

    /// <summary>OFF-03 CA 8 e 13: as vendas e as aprovações sem rede da sessão; null quando nada foi feito sem rede.</summary>
    private async Task<CashSessionOfflineView?> OfflineAsync(CashSession session, CancellationToken cancellationToken)
    {
        var sales = await db.SessionSales.AsNoTracking().Where(s => s.SessionId == session.Id && s.Offline)
            .OrderBy(s => s.CreatedAt).Select(s => s.SaleNumber ?? s.SaleId.ToString()).Take(200).ToListAsync(cancellationToken);
        var approvals = await db.SupervisorApprovals.AsNoTracking().Where(a => a.CashSessionId == session.Id && !a.Online)
            .Select(a => new { a.BeforeAmount, a.AfterAmount }).ToListAsync(cancellationToken);
        return session.OpenedOffline || session.ClosedOffline || sales.Count > 0 || approvals.Count > 0
            ? new CashSessionOfflineView(session.OpenedOffline, session.ClosedOffline, sales, approvals.Count, approvals.Sum(a => Math.Abs(a.BeforeAmount - a.AfterAmount)))
            : null;
    }

    /// <summary>Grava; a sessão (ou a conferência) que mudou desde a leitura vira 409 (B2).</summary>
    private async Task<Error?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyError;
        }
    }

    private IQueryable<CashSession> Filtered(CashSessionFilter f, bool expectedVisible)
    {
        var query = db.CashSessions.AsNoTracking();
        if (f.StoreId is { } store)
        {
            query = query.Where(s => s.StoreId == store);
        }

        if (f.RegisterId is { } register)
        {
            query = query.Where(s => s.RegisterId == register);
        }

        if (f.OperatorId is { } op)
        {
            query = query.Where(s => s.OperatorId == op);
        }

        if (f.Status is { } status)
        {
            // Sem sales.approve, "Enviado" (Closed) cobre também a em conferência, e o filtro "em conferência" não existe (M3).
            query = expectedVisible ? query.Where(s => s.Status == status)
                : status is CashSessionStatus.Closed or CashSessionStatus.PendingReview ? query.Where(s => s.Status == CashSessionStatus.Closed || s.Status == CashSessionStatus.PendingReview)
                : query.Where(s => s.Status == status);
        }

        if (f.From is { } from)
        {
            var start = BusinessTime.StartOfDayUtc(from);
            query = query.Where(s => s.OpenedAt >= start);
        }

        if (f.To is { } to)
        {
            var end = BusinessTime.StartOfDayUtc(to.AddDays(1));
            query = query.Where(s => s.OpenedAt < end);
        }

        return query;
    }

    private static bool Awaiting(CashSession s, IReadOnlyDictionary<Guid, int> arrived) =>
        s.Status == CashSessionStatus.Counted && arrived.GetValueOrDefault(s.Id) < (s.DeviceSalesCount ?? 0);

    private async Task<IReadOnlyDictionary<Guid, int>> ArrivedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await CashClosingService.ArrivedSales(db, ids).GroupBy(s => s.SessionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    private async Task<(IReadOnlyDictionary<Guid, string> Stores, IReadOnlyDictionary<Guid, int> Registers)> PlacesAsync(CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var registers = await db.Registers.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        return (stores, registers);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids.Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido";
        }

        return names;
    }

    private static Error? ReasonError(string? reason)
    {
        var text = reason?.Trim() ?? string.Empty;
        return text.Length is 0 or > MaxReasonLength || PosText.LooksLikeCardNumber(text)
            ? CashClosingService.Field("reason", $"Informe o motivo, com até {MaxReasonLength} caracteres.")
            : null;
    }
}
