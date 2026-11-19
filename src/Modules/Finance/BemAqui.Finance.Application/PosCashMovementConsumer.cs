using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Pos.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Consumidor do Financeiro para o dinheiro do caixa do PDV (CX-01 CA 9): cada movimento da sessão de caixa vira uma transferência entre o Cofre e a
/// conta Caixa do PDV (a do caixa), ligada ao movimento:
/// - abertura (fundo de troco) e suprimento: Cofre → Caixa do PDV;
/// - sangria: Caixa do PDV → Cofre.
/// Reprocessar não duplica: a transferência é única por movimento (<c>pos_cash_movement_id</c>, índice único). Fundo de troco zero não transfere nada.
/// O Cofre é a conta do pacote do Comércio (<c>seed_key = cofre</c>) ou uma conta Caixa ativa chamada "Cofre"; sem ela, o consumidor cria a conta
/// "Cofre" (tipo Caixa) para o dinheiro não ficar sem destino. Fora das travas de tela (conta nova, período fechado): o fato já aconteceu na gaveta.
/// </summary>
public sealed class PosCashMovementConsumer(IFinanceDb db, TimeProvider clock) : IIntegrationEventConsumer<CashMovementRecorded>
{
    public const string SafeSeedKey = "cofre";
    public const string SafeName = "Cofre";

    public async Task HandleAsync(CashMovementRecorded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.Amount <= 0 || await db.BankTransfers.AnyAsync(t => t.PosCashMovementId == integrationEvent.MovementId, cancellationToken))
        {
            return;
        }

        var drawer = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == integrationEvent.CashAccountId, cancellationToken)
            ?? throw new InvalidOperationException("A conta do caixa " + integrationEvent.CashAccountId + " não existe no Financeiro.");
        var safe = await SafeAsync(cancellationToken);
        var toDrawer = integrationEvent.Kind != CashMovementRecorded.CashOut;
        var (from, to) = toDrawer ? (safe, drawer.Id) : (drawer.Id, safe);
        var now = clock.GetUtcNow();
        var transfer = new BankTransfer
        {
            FromAccountId = from, ToAccountId = to, Amount = integrationEvent.Amount, Date = integrationEvent.BusinessDate,
            Description = Describe(integrationEvent), PosCashMovementId = integrationEvent.MovementId, CreatedAt = now,
        };
        db.BankTransfers.Add(transfer);
        db.FinancialTransactions.AddRange(Line(transfer, from, -transfer.Amount, now), Line(transfer, to, transfer.Amount, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>"Sangria do caixa 01: Depósito no banco", "Suprimento do caixa 01: Troco" ou "Fundo de troco do caixa 01".</summary>
    public static string Describe(CashMovementRecorded e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var register = e.RegisterNumber.ToString("00", CultureInfo.InvariantCulture);
        var text = e.Kind switch
        {
            CashMovementRecorded.CashOut => $"Sangria do caixa {register}: {e.Reason}",
            CashMovementRecorded.CashIn => $"Suprimento do caixa {register}: {e.Reason}",
            _ => $"Fundo de troco do caixa {register}",
        };
        return text.Length <= 200 ? text : text[..200];
    }

    private async Task<Guid> SafeAsync(CancellationToken cancellationToken)
    {
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

    private static FinancialTransaction Line(BankTransfer transfer, Guid account, decimal amount, DateTimeOffset now) => new()
    {
        BankAccountId = account, Date = transfer.Date, Amount = amount, Type = TransactionType.Transfer, Description = transfer.Description,
        TransferId = transfer.Id, CreatedAt = now,
    };
}
