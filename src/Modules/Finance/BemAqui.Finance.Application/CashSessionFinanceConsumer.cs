using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>
/// Fechamento de caixa no Financeiro (CX-03). No <see cref="CashSessionClosed"/>:
/// <list type="bullet">
/// <item>os pagamentos da sessão (lidos do <c>Sales</c> pelo <see cref="ISaleLookup"/>) viram os títulos da sessão pelo <see cref="ISaleReceivables"/>
/// ("grava o valor": a segunda entrega não duplica), agrupados por <see cref="CashSessionReceivables"/>;</item>
/// <item>o dinheiro entra na conta do caixa (baixa do título de dinheiro, origem <see cref="SettlementSource.Pos"/>) e o Pix "Não localizado" na conta
/// Quebra de caixa;</item>
/// <item>a quebra do dinheiro vira transferência entre a conta do caixa e a Quebra de caixa (falta: caixa → quebra; sobra: quebra → caixa), única
/// por sessão e reabertura. Com o recolhimento (CX-02) e os movimentos da CX-01, o saldo da conta do caixa fecha no fundo que fica.</item>
/// </list>
/// No <see cref="CashSessionReopened"/>, estorna as baixas e a transferência da quebra e zera os títulos (o próximo fechamento os reabre). Título
/// com baixa feita fora do fechamento (conciliado) não é mexido: a reabertura é recusada antes, no <c>Pos</c> (<see cref="ICashSessionFinance"/>).
/// Uma trava por sessão põe em fila as duas entregas.
/// </summary>
public sealed partial class CashSessionFinanceConsumer(
    IFinanceDb db, ISaleLookup sales, IPaymentMethods methods, ISaleReceivables receivables, TimeProvider clock, ILogger<CashSessionFinanceConsumer> logger)
    : IIntegrationEventConsumer<CashSessionClosed>, IIntegrationEventConsumer<CashSessionReopened>
{
    public const string BreakSeedKey = "quebra-de-caixa";
    public const string BreakAccountName = "Quebra de caixa";
    private const string BreakLockKey = "finance.pos-cash-break";

    public async Task HandleAsync(CashSessionClosed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var e = integrationEvent;
        await LockAsync("finance.cash-session:" + e.SessionId.ToString("N"), cancellationToken);
        var facts = await sales.SessionPaymentsAsync(e.SessionId, cancellationToken);
        var methodIds = facts.Select(f => f.Payment.PaymentMethodId).Concat(e.Breaks.Where(b => b.PaymentMethodId != null).Select(b => b.PaymentMethodId!.Value)).Distinct().ToList();
        // Inclusive forma desativada ou excluída depois da venda: a venda já aconteceu com ela.
        var known = await db.PaymentMethods.IgnoreQueryFilters().AsNoTracking().Where(m => methodIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type, m.AcquirerPartyId }).ToDictionaryAsync(m => m.Id, cancellationToken);
        var payments = new List<CashSessionPayment>();
        foreach (var (date, fact) in facts)
        {
            if (!known.TryGetValue(fact.PaymentMethodId, out var method))
            {
                throw new InvalidOperationException("Forma de pagamento " + fact.PaymentMethodId + " da venda não existe no Financeiro.");
            }

            CardBrand? brand = Enum.TryParse<CardBrand>(fact.CardBrand, ignoreCase: true, out var parsed) ? parsed : null;
            var terms = await methods.FeeAsync(method.Id, brand ?? CardBrand.Any, Math.Max(1, fact.Installments), date, cancellationToken);
            payments.Add(new CashSessionPayment(date, fact.SaleId, method.Id, method.Type, method.Name, fact.AcquirerPartyId ?? method.AcquirerPartyId, brand, fact.Installments,
                fact.Amount, terms?.FeePercent ?? 0m, terms?.SettlementDays ?? 0, fact.CustomerId));
        }

        var pixNotFound = e.Breaks.Where(b => b.PaymentMethodId is { } id && b.PixNotFound > 0 && known.TryGetValue(id, out var m) && m.Type == PaymentMethodType.Pix)
            .GroupBy(b => b.PaymentMethodId!.Value).ToDictionary(g => g.Key, g => g.Sum(b => b.PixNotFound));
        var summaries = CashSessionReceivables.Build(e.SessionId, payments, pixNotFound, e.BusinessDate);
        foreach (var summary in summaries)
        {
            var written = await receivables.PostAsync(summary, cancellationToken);
            if (written.IsFailure)
            {
                throw new InvalidOperationException("Recebível do caixa recusado: " + written.Error!.Message);
            }
        }

        // Grupo de um fechamento anterior (antes da reabertura) que deixou de existir: zera.
        var keys = summaries.Select(s => s.Key).ToList();
        foreach (var day in await db.Receivables.Where(r => r.CashSessionId == e.SessionId && r.Status != ReceivableStatus.Cancelled).Select(r => r.IssueDate).Distinct().ToListAsync(cancellationToken))
        {
            await receivables.ZeroMissingAsync(day, CashSessionReceivables.Channel, e.SessionId, keys, cancellationToken);
        }

        var drawer = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == e.CashAccountId, cancellationToken);
        if (drawer is null || !drawer.IsActive)
        {
            LogDrawerMissing(logger, e.SessionId, e.RegisterId, e.CashAccountId);
            return;
        }

        var cashMethods = known.Values.Where(m => m.Type == PaymentMethodType.Cash).Select(m => m.Id).ToHashSet();
        var toSettle = summaries.Where(s => s.Net > 0 && (s.Key.Break || cashMethods.Contains(s.Key.PaymentMethodId))).ToList();
        var cashDifference = e.Breaks.Where(b => b.PaymentMethodId is null || cashMethods.Contains(b.PaymentMethodId.Value)).Sum(b => b.Difference);
        Guid? quebra = toSettle.Any(s => s.Key.Break) || cashDifference != 0 ? await BreakAccountAsync(cancellationToken) : null;
        var now = clock.GetUtcNow();
        foreach (var summary in toSettle)
        {
            var key = summary.Key.Text;
            var receivable = await db.Receivables.FirstAsync(r => r.SaleGroupKey == key, cancellationToken);
            if (receivable.Balance <= 0 || !receivable.IsOpen)
            {
                continue;
            }

            var account = summary.Key.Break ? quebra!.Value : drawer.Id;
            var (date, note) = await DateAsync(summary.Key.BusinessDate, [account], cancellationToken);
            var active = await db.ReceivableSettlements.Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).ToListAsync(cancellationToken);
            ReceivableService.AddPosSettlement(db, receivable, active, account, date, note, now);
        }

        var breakId = BreakMovementId(e.SessionId, e.ReopenCount);
        if (cashDifference != 0 && quebra is { } breakAccount && breakAccount != drawer.Id && !await db.BankTransfers.AnyAsync(t => t.PosCashMovementId == breakId, cancellationToken))
        {
            var (from, to) = cashDifference < 0 ? (drawer.Id, breakAccount) : (breakAccount, drawer.Id);
            var (date, note) = await DateAsync(e.BusinessDate, [from, to], cancellationToken);
            var kind = cashDifference < 0 ? "Falta" : "Sobra";
            BankMovementService.AddPosCashTransfer(db, new BankTransfer
            {
                FromAccountId = from, ToAccountId = to, Amount = Math.Abs(cashDifference), Date = date, Description = $"{kind} no fechamento do caixa de {e.BusinessDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                PosCashMovementId = breakId, PosCashSessionId = e.SessionId, BackdateReason = note, CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(CashSessionReopened integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var e = integrationEvent;
        await LockAsync("finance.cash-session:" + e.SessionId.ToString("N"), cancellationToken);
        var titles = await db.Receivables.Where(r => r.CashSessionId == e.SessionId && r.Status != ReceivableStatus.Cancelled).ToListAsync(cancellationToken);
        var ids = titles.Select(r => r.Id).ToList();
        var settlements = await db.ReceivableSettlements.Where(s => ids.Contains(s.ReceivableId) && s.ReversedAt == null).ToListAsync(cancellationToken);
        if (settlements.Any(s => s.Source != SettlementSource.Pos))
        {
            // O Pos recusa a reabertura nesse caso (CA 7); se chegou aqui, nada muda e o log aponta a sessão.
            LogReconciled(logger, e.SessionId);
            return;
        }

        var today = BusinessTime.Today(clock);
        var now = clock.GetUtcNow();
        foreach (var settlement in settlements)
        {
            ReceivableService.ReversePosSettlement(db, titles.First(r => r.Id == settlement.ReceivableId), settlement, today, now);
        }

        var breakIds = Enumerable.Range(0, Math.Max(1, e.ReopenCount + 1)).Select(g => (Guid?)BreakMovementId(e.SessionId, g)).ToList();
        foreach (var transfer in await db.BankTransfers.Where(t => t.PosCashSessionId == e.SessionId && t.ReversedAt == null && breakIds.Contains(t.PosCashMovementId)).ToListAsync(cancellationToken))
        {
            await BankMovementService.ReversePosCashTransferAsync(db, transfer, today, ReceivableService.PosReopenReason, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var day in titles.Select(r => r.IssueDate).Distinct())
        {
            await receivables.ZeroMissingAsync(day, CashSessionReceivables.Channel, e.SessionId, [], cancellationToken);
        }
    }

    /// <summary>Id da transferência da quebra do dinheiro: único por sessão e reabertura (o índice único de <c>pos_cash_movement_id</c> segura a repetição).</summary>
    public static Guid BreakMovementId(Guid sessionId, int reopenCount)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("cx03-break:" + sessionId.ToString("N") + ":" + reopenCount.ToString(CultureInfo.InvariantCulture)));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Data do lançamento: a do caixa; em período de conciliação já fechado, hoje, com a observação (nunca retroativo).</summary>
    private async Task<(DateOnly Date, string? Note)> DateAsync(DateOnly date, IReadOnlyList<Guid> accounts, CancellationToken ct)
    {
        var closedUntil = await db.BankReconciliationClosings.AsNoTracking().Where(c => accounts.Contains(c.BankAccountId)).MaxAsync(c => (DateOnly?)c.UntilDate, ct);
        return closedUntil is { } until && date <= until
            ? (BusinessTime.Today(clock), $"Fechamento do caixa de {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, período já conciliado: lançado hoje.")
            : (date, null);
    }

    /// <summary>A conta "Quebra de caixa" (do pacote do Comércio ou uma conta Caixa ativa com esse nome); sem ela, cria (tipo Caixa).</summary>
    private async Task<Guid> BreakAccountAsync(CancellationToken cancellationToken)
    {
        await LockAsync(BreakLockKey, cancellationToken);
        var account = await db.BankAccounts.Where(a => a.IsActive && a.Kind == BankAccountKind.Cash && (a.SeedKey == BreakSeedKey || a.Name == BreakAccountName))
            .OrderByDescending(a => a.SeedKey == BreakSeedKey).ThenBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (account is not null)
        {
            return account.Value;
        }

        var created = new BankAccount { Name = BreakAccountName, Kind = BankAccountKind.Cash, CreatedAt = clock.GetUtcNow() };
        db.BankAccounts.Add(created);
        return created.Id;
    }

    private async Task LockAsync(string key, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fechamento {SessionId} do caixa {RegisterId} sem baixa do dinheiro nem quebra: a conta do caixa {AccountId} não existe ou está inativa no Financeiro.")]
    private static partial void LogDrawerMissing(ILogger logger, Guid sessionId, Guid registerId, Guid accountId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reabertura do caixa {SessionId} sem estorno no Financeiro: a sessão tem recebimento conciliado.")]
    private static partial void LogReconciled(ILogger logger, Guid sessionId);
}
