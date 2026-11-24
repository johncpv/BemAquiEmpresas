using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

/// <summary>"Baixar recebíveis de cartão" a partir de um depósito (CX-03 CA 8): os títulos da adquirente e as versões que a tela abriu.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LineCardSettlementInput(IReadOnlyList<VersionedRef> Receivables, uint LineVersion, string? BackdateReason = null);

/// <summary>
/// Resultado do depósito da adquirente: a conciliação, o líquido previsto somado, o recebido e a diferença (recebido − previsto); com
/// diferença, o título que ficou com saldo em aberto.
/// </summary>
public sealed record CardSettlementResult(ReconcileResult Reconciliation, decimal Expected, decimal Received, decimal Difference, Guid? OpenReceivableId, string? OpenReceivableNumber);

/// <summary>Título de adquirente em aberto para escolher no depósito.</summary>
public sealed record CardReceivableCandidate(Guid Id, string Number, string Description, Guid PartyId, DateOnly? DueDate, decimal? GrossAmount, decimal? ExpectedFee, decimal Balance, uint Version);

public sealed partial class ReconciliationService
{
    public const string CardDepositAboveCode = "card_deposit_above_expected";

    /// <summary>Títulos de adquirente (origem Cartão) em aberto, por vencimento, para o depósito da linha (até 200).</summary>
    public async Task<Result<IReadOnlyList<CardReceivableCandidate>>> CardCandidatesAsync(Guid accountId, DateOnly? until, CancellationToken cancellationToken = default)
    {
        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var query = db.Receivables.AsNoTracking().Where(r => r.Origin == DocumentOrigin.CardSettlement && (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid));
        if (until is { } date)
        {
            query = query.Where(r => r.DueDate <= date);
        }

        return await query.OrderBy(r => r.DueDate).ThenBy(r => r.Number).Take(200)
            .Select(r => new CardReceivableCandidate(r.Id, r.Number, r.Description, r.PartyId, r.DueDate, r.GrossAmount, r.ExpectedFee, r.Amount - r.PaidAmount, r.Version))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Um depósito da adquirente baixa vários títulos (CX-03 CA 8): a adquirente deposita um valor só para vários dias, bandeiras e formas. Os
    /// títulos são da origem Cartão, em aberto e da mesma adquirente; cada um é baixado pelo saldo (o líquido previsto, já sem a taxa), com a data
    /// e a conta da linha, e a linha fica conciliada com todos os recebimentos, numa transação. Depósito menor que o previsto: o último título (por
    /// vencimento) recebe só o que sobrou e a diferença fica apontada nele, em aberto. Depósito maior: 422 (falta escolher título).
    /// </summary>
    public async Task<Result<CardSettlementResult>> SettleCardsAsync(Guid accountId, Guid lineId, LineCardSettlementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var refs = (input.Receivables ?? []).DistinctBy(r => r.Id).ToList();
        if (refs.Count is 0 or > MaxItems)
        {
            return ReceivableService.Invalid("receivables", "Escolha de 1 a 50 recebíveis de cartão.");
        }

        var loaded = await PendingLineAsync(accountId, lineId, input.LineVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var line = loaded.Value!;
        if (line.Amount <= 0)
        {
            return ReceivableService.Invalid("receivables", "Só uma entrada do banco baixa recebíveis de cartão.");
        }

        if (await SegregationAsync([line], cancellationToken) is { } segregation)
        {
            return segregation;
        }

        var ids = refs.Select(r => r.Id).ToList();
        var titles = await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        if (titles.Count != ids.Count || titles.Any(r => r.Origin != DocumentOrigin.CardSettlement))
        {
            return ReceivableService.Invalid("receivables", "Escolha só recebíveis de cartão (títulos contra a adquirente).");
        }

        if (titles.Any(r => !r.IsOpen || r.Balance <= 0))
        {
            return Error.Conflict("receivable_not_open", "Um dos recebíveis já foi baixado ou cancelado. Recarregue.");
        }

        if (titles.Select(r => r.PartyId).Distinct().Count() > 1)
        {
            return ReceivableService.Invalid("receivables", "Escolha recebíveis de uma adquirente só: o depósito vem de uma adquirente.");
        }

        var expected = titles.Sum(r => r.Balance);
        if (line.Amount > expected)
        {
            var message = $"O depósito ({ReceivableService.Money(line.Amount)}) passa do líquido previsto dos recebíveis escolhidos ({ReceivableService.Money(expected)}). Inclua os que faltam.";
            return Error.Validation(CardDepositAboveCode, message, new Dictionary<string, string[]> { ["receivables"] = [message] });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var known = db.FinancialTransactions.Local.Select(t => t.Id).ToHashSet();
        var ordered = titles.OrderBy(r => r.DueDate).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        var left = line.Amount;
        (Guid Id, string Number)? open = null;
        foreach (var title in ordered)
        {
            var principal = Math.Min(title.Balance, left);
            if (principal <= 0)
            {
                // Sobrou título sem dinheiro: o depósito não cobriu; a tela recusa antes, mas o serviço também.
                return ReceivableService.Invalid("receivables", "O depósito não chega a todos os recebíveis escolhidos. Tire os que não vieram neste depósito.");
            }

            var version = refs.First(r => r.Id == title.Id).Version;
            var settled = await receivables.SettleAsync(title.Id, new SettlementInput(accountId, line.Date, principal, 0m, 0m, 0m, input.BackdateReason, version), cancellationToken);
            if (settled.IsFailure)
            {
                return settled.Error!;
            }

            left -= principal;
            if (principal < title.Balance)
            {
                open = (title.Id, title.Number);
            }
        }

        var receipts = db.FinancialTransactions.Local.Where(t => !known.Contains(t.Id) && t.ReceivableId != null && ids.Contains(t.ReceivableId.Value) && t.ReversesTransactionId == null).ToList();
        var closedReason = await period.IsClosedAsync(accountId, line.Date, cancellationToken) ? input.BackdateReason?.Trim() : null;
        var reconciliation = Record(accountId, ReconciliationKind.Settlement, null, $"Depósito da adquirente: {titles.Count} recebível(is) de cartão", closedReason, [line], receipts);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CardSettlementResult(Result(reconciliation, [line], receipts), expected, line.Amount, line.Amount - expected, open?.Id, open?.Number);
    }
}
