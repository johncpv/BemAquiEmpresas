using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Pos.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>
/// Consumidor do Financeiro para o dinheiro do caixa do PDV (CX-01 CA 9): cada movimento da sessão de caixa vira uma transferência entre o Cofre e a
/// conta Caixa do PDV (a do caixa), ligada ao movimento:
/// - abertura (fundo de troco) e suprimento: Cofre → Caixa do PDV;
/// - sangria: Caixa do PDV → Cofre.
/// Reprocessar não duplica: a transferência é única por movimento (<c>pos_cash_movement_id</c>, índice único). Fundo de troco zero não transfere nada.
/// O Cofre é a conta do pacote do Comércio (<c>seed_key = cofre</c>) ou uma conta Caixa ativa chamada "Cofre"; sem ela, o consumidor cria a conta
/// "Cofre" (tipo Caixa) para o dinheiro não ficar sem destino. Fora da trava de conta nova (o fato já aconteceu na gaveta); data em período de
/// conciliação fechado entra com a data de hoje e a observação em <c>BackdateReason</c>, nunca retroativa. Conta do caixa inexistente, inativa ou
/// igual ao Cofre: nada é gravado e fica o log de erro (o movimento continua no Pos; a CX-03 e a conciliação acham a falta).
/// </summary>
public sealed partial class PosCashMovementConsumer(IFinanceDb db, TimeProvider clock, ILogger<PosCashMovementConsumer> logger) : IIntegrationEventConsumer<CashMovementRecorded>
{
    public const string SafeSeedKey = "cofre";
    public const string SafeName = "Cofre";
    private const string SafeLockKey = "finance.pos-cash-safe";

    public async Task HandleAsync(CashMovementRecorded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.Amount <= 0 || await db.BankTransfers.AnyAsync(t => t.PosCashMovementId == integrationEvent.MovementId, cancellationToken))
        {
            return;
        }

        var drawer = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == integrationEvent.CashAccountId, cancellationToken);
        if (drawer is null || !drawer.IsActive)
        {
            LogDrawerMissing(logger, integrationEvent.MovementId, integrationEvent.RegisterId, integrationEvent.CashAccountId);
            return;
        }

        var safe = await SafeAsync(cancellationToken);
        if (safe == drawer.Id)
        {
            LogDrawerIsSafe(logger, integrationEvent.MovementId, integrationEvent.RegisterId);
            return;
        }

        var toDrawer = integrationEvent.Kind != CashMovementRecorded.CashOut;
        var (from, to) = toDrawer ? (safe, drawer.Id) : (drawer.Id, safe);
        // Período de conciliação fechado em qualquer das contas: entra hoje, com a data do caixa na observação (nunca retroativo).
        var date = integrationEvent.BusinessDate;
        var closedUntil = await db.BankReconciliationClosings.AsNoTracking().Where(c => c.BankAccountId == from || c.BankAccountId == to)
            .MaxAsync(c => (DateOnly?)c.UntilDate, cancellationToken);
        string? note = null;
        if (closedUntil is { } until && date <= until)
        {
            note = $"Movimento do caixa de {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, período já conciliado: lançado hoje.";
            date = BusinessTime.Today(clock);
        }

        var now = clock.GetUtcNow();
        var transfer = new BankTransfer
        {
            FromAccountId = from, ToAccountId = to, Amount = integrationEvent.Amount, Date = date,
            Description = Describe(integrationEvent), PosCashMovementId = integrationEvent.MovementId, BackdateReason = note, CreatedAt = now,
        };
        BankMovementService.AddPosCashTransfer(db, transfer);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>"Sangria do caixa 01", "Suprimento do caixa 01" ou "Fundo de troco do caixa 01". O motivo fica no Pos (evento leva só ids, ADR 0037 §2).</summary>
    public static string Describe(CashMovementRecorded e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var register = e.RegisterNumber.ToString("00", CultureInfo.InvariantCulture);
        var text = e.Kind switch
        {
            CashMovementRecorded.CashOut => $"Sangria do caixa {register}",
            CashMovementRecorded.CashIn => $"Suprimento do caixa {register}",
            _ => $"Fundo de troco do caixa {register}",
        };
        return text;
    }

    private async Task<Guid> SafeAsync(CancellationToken cancellationToken)
    {
        // Os consumidores rodam em paralelo: a trava (até o fim da transação) impede dois "Cofre" criados ao mesmo tempo.
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({SafeLockKey}, 0))) AS l").ToListAsync(cancellationToken);
        var safe = await db.BankAccounts.Where(a => a.IsActive && a.Kind == BankAccountKind.Cash && (a.SeedKey == SafeSeedKey || a.Name == SafeName))
            .OrderByDescending(a => a.SeedKey == SafeSeedKey).ThenBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (safe is not null)
        {
            return safe.Value;
        }

        var created = new BankAccount { Name = SafeName, Kind = BankAccountKind.Cash, CreatedAt = clock.GetUtcNow() };
        db.BankAccounts.Add(created);
        return created.Id;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Movimento {MovementId} do caixa {RegisterId} sem transferência: a conta do caixa {AccountId} não existe ou está inativa no Financeiro.")]
    private static partial void LogDrawerMissing(ILogger logger, Guid movementId, Guid registerId, Guid accountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Movimento {MovementId} do caixa {RegisterId} sem transferência: a conta do caixa é o próprio Cofre.")]
    private static partial void LogDrawerIsSafe(ILogger logger, Guid movementId, Guid registerId);

}
