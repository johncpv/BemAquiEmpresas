using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Abertura do caixa vinda do aparelho (evento <c>CashSessionOpened</c>): o id da sessão e o do movimento do fundo de troco são do aparelho.</summary>
/// <remarks>OFF-03 CA 6: <c>Offline</c>, a abertura foi feita sem rede.</remarks>
public sealed record CashSessionOpenInput(Guid SessionId, Guid RegisterId, int DeviceEpoch, Guid OperatorId, decimal OpeningAmount, DateTimeOffset OpenedAt, Guid OpeningMovementId,
    bool Offline = false);

/// <summary>Sangria (<c>CashOutRecorded</c>) ou suprimento (<c>CashInRecorded</c>) vindos do aparelho.</summary>
public sealed record CashMovementInput(Guid MovementId, Guid SessionId, CashMovementKind Kind, decimal Amount, string? Reason, Guid OperatorId, DateTimeOffset OccurredAt, bool AboveExpected);

public sealed record CashSessionView(Guid Id, Guid RegisterId, Guid OperatorId, CashSessionStatus Status, decimal OpeningAmount, DateTimeOffset OpenedAt, bool Replayed);

public sealed record CashMovementView(Guid Id, Guid SessionId, CashMovementKind Kind, decimal Amount, string? Reason, Guid OperatorId, DateTimeOffset OccurredAt, bool AboveExpected, bool Replayed);

/// <summary>
/// Gravação da sessão de caixa e dos movimentos de dinheiro no servidor (CX-01). O aparelho grava primeiro (local-first) e a subida é da OFF-02,
/// que chama este serviço para cada evento <c>CashSessionOpened</c>, <c>CashOutRecorded</c> e <c>CashInRecorded</c>, na transação da ingestão.
/// - Idempotente pelo id do aparelho: reenviar devolve o que já foi gravado, sem duplicar nada (nem o evento do Financeiro).
/// - Uma sessão <c>Open</c> por caixa (CA 2): outra abertura no mesmo caixa é recusada com quem abriu e desde quando (índice único parcial no banco).
/// - Toda ação com dinheiro leva o operador; sangria e suprimento exigem motivo; nenhum valor é recusado por passar do dinheiro calculado (CA 4).
/// - Cada movimento publica <see cref="CashMovementRecorded"/> no outbox; o Financeiro transfere entre o Cofre e o Caixa do PDV (CA 9).
/// </summary>
public sealed class CashSessionIngestion(IPosDb db, IEventPublisher events, IUserDirectory users, TimeProvider clock)
{
    public const string OpenSessionIndex = "ix_cash_sessions_register_open";
    public const decimal MaxAmount = 9_999_999.99m;

    /// <summary>Folga para o relógio do aparelho à frente do servidor (os 2 min da ADR 0026).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    public async Task<Result<CashSessionView>> OpenAsync(CashSessionOpenInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.SessionId, cancellationToken);
        if (existing is not null)
        {
            return existing.RegisterId == input.RegisterId
                ? ToView(existing, replayed: true)
                : Error.Conflict("cash_session_conflict", "Esta sessão de caixa já foi gravada em outro caixa.");
        }

        if (input.OperatorId == Guid.Empty)
        {
            return Error.Validation("operator_required", "Informe o operador.");
        }

        if (input.OpeningAmount < 0 || input.OpeningAmount > MaxAmount || decimal.Round(input.OpeningAmount, 2) != input.OpeningAmount)
        {
            return Error.Validation("invalid_amount", "Informe o fundo de troco em reais, com até 2 casas.");
        }

        if (TimeError(input.OpenedAt, null) is { } time)
        {
            return time;
        }

        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RegisterId, cancellationToken);
        if (register is null)
        {
            return Error.NotFound("register_not_found", "Caixa não encontrado.");
        }

        if (await db.CashMovements.AnyAsync(m => m.Id == input.OpeningMovementId, cancellationToken))
        {
            return Error.Conflict("cash_movement_conflict", "O movimento da abertura já foi gravado com outros dados.");
        }

        if (await OpenConflictAsync(register.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var now = clock.GetUtcNow();
        var session = CashSession.FromDevice(input.SessionId, register.StoreId, register.Id, input.DeviceEpoch, input.OperatorId, input.OpeningAmount, input.OpenedAt, now, input.Offline);
        db.CashSessions.Add(session);
        var opening = CashMovement.FromDevice(input.OpeningMovementId, session.Id, CashMovementKind.Opening, input.OpeningAmount, null, input.OperatorId, input.OpenedAt, false, now);
        db.CashMovements.Add(opening);
        // CX-02 CA 12: o fundo que ficou na gaveta no último fechamento do caixa já está na conta do caixa; a abertura transfere só a diferença
        // (a menos, a sobra volta ao Cofre). O movimento guarda o fundo inteiro (é o dinheiro da gaveta para o esperado).
        var carried = await db.CashSessions.AsNoTracking().Where(s => s.RegisterId == register.Id && s.KeptAmount != null)
            .OrderByDescending(s => s.OpenedAt).Select(s => s.KeptAmount).FirstOrDefaultAsync(cancellationToken) ?? 0m;
        Publish(opening, register, input.OpeningAmount - carried);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, OpenSessionIndex))
        {
            // Duas aberturas do mesmo caixa ao mesmo tempo: a outra gravou primeiro (a transação de quem chama já não aceita consulta).
            return Error.Conflict("cash_session_open", "Este caixa já está aberto.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A mesma abertura (ou o mesmo movimento) gravada por outra entrega ao mesmo tempo.
            return Error.Conflict("cash_session_conflict", "A abertura acabou de ser gravada por outra entrega. Envie de novo para receber a resposta.");
        }

        return ToView(session, replayed: false);
    }

    public async Task<Result<CashMovementView>> RecordMovementAsync(CashMovementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await db.CashMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == input.MovementId, cancellationToken);
        if (existing is not null)
        {
            return existing.SessionId == input.SessionId && existing.Kind == input.Kind
                ? ToView(existing, replayed: true)
                : Error.Conflict("cash_movement_conflict", "Este movimento já foi gravado com outros dados.");
        }

        if (!CashMovement.NeedsReason(input.Kind))
        {
            return Error.Validation("invalid_kind", "Informe uma sangria ou um suprimento.");
        }

        if (input.OperatorId == Guid.Empty)
        {
            return Error.Validation("operator_required", "Informe o operador.");
        }

        if (input.Amount <= 0 || input.Amount > MaxAmount || decimal.Round(input.Amount, 2) != input.Amount)
        {
            return Error.Validation("invalid_amount", "Informe um valor maior que zero, com até 2 casas.");
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > CashMovement.MaxReasonLength)
        {
            return Error.Validation("reason_required", $"Informe o motivo, com até {CashMovement.MaxReasonLength} caracteres.");
        }

        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.SessionId, cancellationToken);
        if (session is null)
        {
            return Error.NotFound("cash_session_not_found", "Sessão de caixa não encontrada.");
        }

        if (session.Status != CashSessionStatus.Open)
        {
            return Error.Conflict("cash_session_closed", "Esta sessão de caixa já foi fechada.");
        }

        if (TimeError(input.OccurredAt, session.OpenedAt) is { } time)
        {
            return time;
        }

        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
        var movement = CashMovement.FromDevice(input.MovementId, session.Id, input.Kind, input.Amount, reason, input.OperatorId, input.OccurredAt, input.AboveExpected, clock.GetUtcNow());
        db.CashMovements.Add(movement);
        Publish(movement, register);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Error.Conflict("cash_movement_conflict", "O movimento acabou de ser gravado por outra entrega. Envie de novo para receber a resposta.");
        }

        return ToView(movement, replayed: false);
    }

    /// <summary>
    /// Janela de horário do evento do aparelho (o horário define a data da transferência no Financeiro): nunca antes da abertura da sessão, nem depois
    /// de agora mais a folga do relógio.
    /// </summary>
    private Error? TimeError(DateTimeOffset at, DateTimeOffset? openedAt) =>
        at > clock.GetUtcNow() + ClockTolerance
            ? Error.Validation("invalid_time", "O horário do aparelho está à frente do servidor. Acerte o relógio do caixa.")
            : openedAt is { } opened && at < opened
                ? Error.Validation("invalid_time", "O horário do movimento é anterior à abertura do caixa.")
                : null;

    /// <summary>"Este caixa já está aberto por Fulano desde 08:02." (CA 2), ou null se o caixa não tem sessão aberta.</summary>
    private async Task<Error?> OpenConflictAsync(Guid registerId, CancellationToken cancellationToken)
    {
        var open = await db.CashSessions.AsNoTracking().Where(s => s.RegisterId == registerId && s.Status == CashSessionStatus.Open)
            .Select(s => new { s.OperatorId, s.OpenedAt }).FirstOrDefaultAsync(cancellationToken);
        if (open is null)
        {
            return null;
        }

        var name = await users.DisplayNameAsync(open.OperatorId, cancellationToken) ?? "outro operador";
        var since = BusinessTime.ToBusiness(open.OpenedAt).ToString("HH:mm", CultureInfo.InvariantCulture);
        return Error.Conflict("cash_session_open", $"Este caixa já está aberto por {name} desde {since}.");
    }

    /// <summary><paramref name="transfer"/>: o valor a transferir quando difere do movimento (abertura com fundo deixado, CX-02); negativo = volta ao Cofre.</summary>
    private void Publish(CashMovement movement, Register register, decimal? transfer = null)
    {
        var amount = transfer ?? movement.Amount;
        var kind = amount < 0 ? CashMovementKind.Deposit : movement.Kind;
        events.Publish(new CashMovementRecorded(
            movement.Id, movement.SessionId, register.StoreId, register.Id, register.Number, register.CashAccountId, kind.ToString(), Math.Abs(amount),
            DateOnly.FromDateTime(BusinessTime.ToBusiness(movement.OccurredAt).DateTime), movement.OccurredAt));
    }

    private static CashSessionView ToView(CashSession s, bool replayed) => new(s.Id, s.RegisterId, s.OperatorId, s.Status, s.OpeningAmount, s.OpenedAt, replayed);

    private static CashMovementView ToView(CashMovement m, bool replayed) =>
        new(m.Id, m.SessionId, m.Kind, m.Amount, m.Reason, m.OperatorId, m.OccurredAt, m.AboveExpected, replayed);
}
