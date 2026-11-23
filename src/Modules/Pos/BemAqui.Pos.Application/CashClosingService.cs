using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Cédula ou moeda contada (CX-02 CA 2, opcional): o valor da peça em centavos e a quantidade.</summary>
public sealed record CashDenominationInput(long Cents, int Quantity);

/// <summary>O contado de uma forma no aparelho: a forma (nula = dinheiro sem forma cadastrada), o valor e, no dinheiro, as peças.</summary>
public sealed record CashCountLineInput(Guid? MethodId, long CountedCents, IReadOnlyList<CashDenominationInput>? Denominations);

/// <summary>
/// A contagem confirmada no aparelho (evento <c>CashCounted</c>, CX-02 CA 2, 4 e 12): o fundo que fica, o id do movimento do recolhimento, as
/// vendas que o aparelho concluiu na sessão (o servidor espera todas subirem) e o contado por forma. Nunca traz esperado nem diferença.
/// </summary>
public sealed record CashCountInput(Guid SessionId, Guid CountId, Guid OperatorId, DateTimeOffset CountedAt, long KeptCents, Guid DepositMovementId, int SalesCount, IReadOnlyList<CashCountLineInput> Lines);

/// <summary>
/// Fechamento do caixa com conferência cega (CX-02), no servidor.
/// - <b>Do aparelho</b> (a subida da OFF-02 chama, na transação do evento): "Fechar caixa" (<c>Open → Counting</c>) e a contagem
///   (<c>→ Counted</c>), com o recolhimento ao Cofre (movimento <c>Deposit</c>, transferência pelo consumidor da CX-01).
/// - <b>Cálculo</b>: com todas as vendas da sessão no servidor, o esperado por forma sai das vendas (pelo <see cref="ICashSessionSales"/>), do fundo
///   e dos movimentos; a quebra é sempre recalculada aqui, nunca aceita do aparelho. Dentro da tolerância da loja em todas as formas: <c>Closed</c> e
///   <see cref="CashSessionClosed"/> no outbox; senão, ou com sangria acima do calculado: <c>PendingReview</c> e o gerente é avisado.
/// - O esperado nunca sai do servidor para o aparelho; na retaguarda, só para quem tem <c>sales.approve</c> (<see cref="CashSessionQueries"/>).
/// </summary>
public sealed class CashClosingService(
    IPosDb db, ICashSessionSales sales, IEventPublisher events, INotificationService notifications, IUserDirectory users, TimeProvider clock)
{
    public const int MaxLines = 30;
    public const long MaxCents = 999_999_999;
    public const string NotificationType = "pos.cash_review";
    public const string Link = "/app/loja/caixas";
    public const string DepositReason = "Recolhimento do fechamento";

    /// <summary>"Fechar caixa" no aparelho (CA 1): a sessão para de receber venda e movimento. Idempotente.</summary>
    public async Task<Result> StartCountAsync(Guid sessionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return Error.NotFound("cash_session_not_found", "Sessão de caixa não encontrada.");
        }

        if (session.Status == CashSessionStatus.Counting)
        {
            return Result.Success();
        }

        if (session.Status != CashSessionStatus.Open)
        {
            return Error.Conflict("cash_session_closed", "Esta sessão de caixa já foi fechada.");
        }

        if (TimeError(at, session.OpenedAt) is { } time)
        {
            return time;
        }

        session.Status = CashSessionStatus.Counting;
        session.CountStartedAt = at;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// A contagem do aparelho (CA 2, 4 e 12). Uma só por fechamento: a mesma contagem reenviada não muda nada; outra contagem da sessão já contada é
    /// recusada (recontar exige supervisor). O recolhimento (dinheiro contado − fundo que fica) vira o movimento <c>Deposit</c>.
    /// </summary>
    public async Task<Result> CountAsync(CashCountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == input.SessionId, cancellationToken);
        if (session is null)
        {
            return Error.NotFound("cash_session_not_found", "Sessão de caixa não encontrada.");
        }

        if (session.CountId == input.CountId)
        {
            return Result.Success();
        }

        if (session.Status is not (CashSessionStatus.Open or CashSessionStatus.Counting))
        {
            return Error.Conflict("cash_count_repeated", "Este caixa já foi contado. Recontar exige o supervisor, com motivo.");
        }

        if (TimeError(input.CountedAt, session.OpenedAt) is { } time)
        {
            return time;
        }

        var methods = await MethodTypesAsync(session.StoreId, cancellationToken);
        if (Validate(input, methods) is { } invalid)
        {
            return invalid;
        }

        string TypeOf(CashCountLineInput line) => line.MethodId is { } id ? methods[id] : CashClosing.CashType;
        var countedCash = input.Lines.Where(l => TypeOf(l) == CashClosing.CashType).Sum(l => l.CountedCents);
        if (input.KeptCents > countedCash)
        {
            return Field("keptCents", "O fundo de troco que fica não pode passar do dinheiro contado.");
        }

        var now = clock.GetUtcNow();
        foreach (var line in input.Lines)
        {
            var type = TypeOf(line);
            db.CashCounts.Add(new CashCount
            {
                SessionId = session.Id, CountId = input.CountId, PaymentMethodId = line.MethodId, MethodType = type, Counted = line.CountedCents / 100m,
                Denominations = line.Denominations is { Count: > 0 } d ? JsonSerializer.Serialize(d.Select(p => new { cents = p.Cents, quantity = p.Quantity })) : null,
                CreatedAt = now,
            });
        }

        var deposit = CashClosing.Deposit(countedCash / 100m, input.KeptCents / 100m);
        session.DepositMovementId = null;
        if (deposit > 0)
        {
            if (await db.CashMovements.AnyAsync(m => m.Id == input.DepositMovementId, cancellationToken))
            {
                return Error.Conflict("cash_movement_conflict", "O movimento do recolhimento já foi gravado com outros dados.");
            }

            var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
            var movement = CashMovement.FromDevice(input.DepositMovementId, session.Id, CashMovementKind.Deposit, deposit, DepositReason, input.OperatorId, input.CountedAt, false, now);
            db.CashMovements.Add(movement);
            Publish(movement, register);
            session.DepositMovementId = movement.Id;
        }

        session.Status = CashSessionStatus.Counted;
        session.CountStartedAt ??= input.CountedAt;
        session.CountId = input.CountId;
        session.CountedBy = input.OperatorId;
        session.CountedAt = input.CountedAt;
        session.KeptAmount = input.KeptCents / 100m;
        session.DepositAmount = deposit;
        session.DeviceSalesCount = input.SalesCount;
        await db.SaveChangesAsync(cancellationToken);
        await EvaluateAsync(session, cancellationToken);
        return Result.Success();
    }

    /// <summary>Uma venda da sessão chegou (ou saiu da quarentena): se a sessão estava esperando as vendas para fechar, tenta de novo.</summary>
    public async Task OnSaleArrivedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session?.Status == CashSessionStatus.Counted)
        {
            await db.SaveChangesAsync(cancellationToken);
            await EvaluateAsync(session, cancellationToken);
        }
    }

    /// <summary>
    /// Calcula a quebra (CA 5 e 6) quando todas as vendas que o aparelho concluiu já estão no servidor; antes disso a sessão fica <c>Counted</c>
    /// ("Aguardando envio das vendas").
    /// </summary>
    public async Task EvaluateAsync(CashSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Status != CashSessionStatus.Counted)
        {
            return;
        }

        var arrived = await db.SessionSales.CountAsync(s => s.SessionId == session.Id, cancellationToken);
        if (arrived < (session.DeviceSalesCount ?? 0))
        {
            return;
        }

        var lines = await RecalculateAsync(session, cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == session.StoreId, cancellationToken);
        var reasons = CashReviewReasons.None;
        if (!CashClosing.WithinTolerance(lines, store.CashBreakTolerance))
        {
            reasons |= CashReviewReasons.BreakAboveTolerance;
        }

        if (await db.CashMovements.AnyAsync(m => m.SessionId == session.Id && m.Kind == CashMovementKind.CashOut && m.AboveExpected, cancellationToken))
        {
            reasons |= CashReviewReasons.CashOutAboveExpected;
        }

        session.ReviewReasons = reasons;
        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
        if (reasons == CashReviewReasons.None)
        {
            Close(session, register, session.CountedBy ?? session.OperatorId, session.CountedAt ?? clock.GetUtcNow(), lines);
        }
        else
        {
            session.Status = CashSessionStatus.PendingReview;
            var title = string.Create(CultureInfo.InvariantCulture, $"Fechamento do caixa {register.Number:00} da loja {store.Name} em conferência.");
            foreach (var user in await users.UsersWithPermissionAsync("sales.approve", cancellationToken))
            {
                if (user != session.OperatorId && user != session.CountedBy)
                {
                    notifications.Notify(user, NotificationType, title, Link + "?sessionId=" + session.Id);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Refaz o esperado, o Pix não localizado e a diferença de cada forma da contagem vigente e grava nas linhas (as formas com venda e sem contagem
    /// entram com contado zero). Não muda a situação da sessão.
    /// </summary>
    public async Task<IReadOnlyList<CashClosingLine>> RecalculateAsync(CashSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var methods = await MethodTypesAsync(session.StoreId, cancellationToken);
        var pixMethods = methods.Where(m => m.Value == CashClosing.PixType).Select(m => m.Key).ToList();
        var summary = await sales.SummaryAsync(session.Id, pixMethods, cancellationToken);
        var movements = await db.CashMovements.AsNoTracking()
            .Where(m => m.SessionId == session.Id && m.Kind != CashMovementKind.Opening && m.Id != session.DepositMovementId)
            .Select(m => new { m.Kind, m.Amount }).ToListAsync(cancellationToken);
        var rows = await db.CashCounts.Where(c => c.SessionId == session.Id && c.CountId == session.CountId).ToListAsync(cancellationToken);
        var notFound = await db.CashPixChecks.AsNoTracking().Where(p => p.SessionId == session.Id && p.Status == PixCheckStatus.NotFound)
            .GroupBy(p => p.PaymentMethodId).Select(g => new { g.Key, Total = g.Sum(p => p.Amount) }).ToDictionaryAsync(g => g.Key, g => g.Total, cancellationToken);
        var drawerExtra = CashClosing.ExpectedDrawer(session.OpeningAmount, 0m, movements.Select(m => (m.Kind, m.Amount)));
        var lines = CashClosing.Lines(summary.ByMethod.ToDictionary(m => m.PaymentMethodId, m => m.Amount), methods,
            rows.Select(r => new CashCountedLine(r.PaymentMethodId, r.MethodType, r.Counted)).ToList(), drawerExtra, notFound);
        var now = clock.GetUtcNow();
        foreach (var line in lines)
        {
            var row = rows.FirstOrDefault(r => line.MethodType == CashClosing.CashType ? r.MethodType == CashClosing.CashType : r.PaymentMethodId == line.MethodId);
            if (row is null && session.CountId is { } countId)
            {
                row = new CashCount { SessionId = session.Id, CountId = countId, PaymentMethodId = line.MethodId, MethodType = line.MethodType, Counted = 0m, CreatedAt = now };
                db.CashCounts.Add(row);
                rows.Add(row);
            }

            if (row is not null)
            {
                row.Expected = line.Expected;
                row.PixNotFound = line.PixNotFound;
                row.Difference = line.Difference;
            }
        }

        return lines;
    }

    /// <summary>Leva a sessão a <c>Closed</c> e publica o <see cref="CashSessionClosed"/> (só aqui: nunca em <c>Counted</c>).</summary>
    internal void Close(CashSession session, Register register, Guid closedBy, DateTimeOffset closedAt, IReadOnlyList<CashClosingLine> lines)
    {
        session.Status = CashSessionStatus.Closed;
        session.ClosedAt = closedAt;
        session.ClosedBy = closedBy;
        events.Publish(new CashSessionClosed(
            session.Id, session.StoreId, session.RegisterId, register.CashAccountId, session.OperatorId, session.OpenedAt, closedAt,
            DateOnly.FromDateTime(BusinessTime.ToBusiness(session.OpenedAt).DateTime), session.KeptAmount ?? 0m, session.DepositAmount ?? 0m, session.ReopenCount,
            lines.Select(l => new CashSessionBreak(l.MethodId, l.Expected, l.Counted - l.PixNotFound, l.Difference)).ToList()));
    }

    internal void Publish(CashMovement movement, Register register) =>
        events.Publish(new CashMovementRecorded(
            movement.Id, movement.SessionId, register.StoreId, register.Id, register.Number, register.CashAccountId, movement.Kind.ToString(), movement.Amount,
            DateOnly.FromDateTime(BusinessTime.ToBusiness(movement.OccurredAt).DateTime), movement.OccurredAt));

    /// <summary>O tipo de cada forma de pagamento da loja (inclusive as desativadas depois da venda), do modelo de leitura do caixa.</summary>
    internal async Task<IReadOnlyDictionary<Guid, string>> MethodTypesAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var payloads = await db.CatalogItems.AsNoTracking().Where(i => i.StoreId == storeId && i.Kind == CatalogItemKind.PaymentMethod).Select(i => i.Payload).ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, string>();
        foreach (var payload in payloads)
        {
            var method = JsonSerializer.Deserialize<PosPaymentMethodPayload>(payload, PosCatalogProjector.Json);
            if (method is not null)
            {
                result[method.Id] = method.Type.ToString();
            }
        }

        return result;
    }

    private static Error? Validate(CashCountInput input, IReadOnlyDictionary<Guid, string> methods)
    {
        if (input.OperatorId == Guid.Empty || input.CountId == Guid.Empty)
        {
            return Field("countId", "Contagem sem id ou sem operador.");
        }

        if (input.Lines is null || input.Lines.Count is 0 or > MaxLines)
        {
            return Field("lines", $"Informe de 1 a {MaxLines} formas na contagem.");
        }

        if (input.SalesCount is < 0 or > 1_000_000 || input.KeptCents is < 0 or > MaxCents)
        {
            return Field("salesCount", "Contagem com número de vendas ou fundo de troco inválido.");
        }

        if (input.Lines.Select(l => l.MethodId).Distinct().Count() != input.Lines.Count)
        {
            return Field("lines", "Forma de pagamento repetida na contagem.");
        }

        var cashLines = 0;
        foreach (var line in input.Lines)
        {
            if (line.CountedCents is < 0 or > MaxCents)
            {
                return Field("lines", "Valor contado inválido (centavos inteiros, de zero a R$ 9.999.999,99).");
            }

            if (line.MethodId is { } id && !methods.ContainsKey(id))
            {
                return Field("lines", "Forma de pagamento desconhecida nesta loja.");
            }

            var cash = line.MethodId is not { } m || methods[m] == CashClosing.CashType;
            cashLines += cash ? 1 : 0;
            if (line.Denominations is { Count: > 0 } pieces)
            {
                if (!cash || pieces.Count > 30 || pieces.Any(p => p.Cents is <= 0 or > 100_000 || p.Quantity is < 0 or > 100_000)
                    || pieces.Sum(p => p.Cents * p.Quantity) != line.CountedCents)
                {
                    return Field("lines", "A contagem por cédula e moeda não fecha com o dinheiro contado.");
                }
            }
        }

        return cashLines > 1 ? Field("lines", "Informe o dinheiro da gaveta numa linha só.") : null;
    }

    private Error? TimeError(DateTimeOffset at, DateTimeOffset openedAt) =>
        at > clock.GetUtcNow() + CashSessionIngestion.ClockTolerance
            ? Error.Validation("invalid_time", "O horário do aparelho está à frente do servidor. Acerte o relógio do caixa.")
            : at < openedAt ? Error.Validation("invalid_time", "O horário do fechamento é anterior à abertura do caixa.") : null;

    internal static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
