using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Devolução, troca e vale-troca no caixa (PDV-06). Só com rede e só no caixa do aparelho (o caixa vem do cookie, nunca do corpo): a venda original é
/// conferida no servidor. Pedir (<see cref="RequestAsync"/>) valida itens, quantidades, prazo e reembolso; aprovar (<see cref="ApproveAsync"/>) conclui
/// tudo numa transação: a venda original muda de situação, o estoque recebe a entrada de devolução (ou a perda), e o dinheiro sai da sessão, nasce o
/// vale-troca ou o estorno no cartão fica registrado. Documentos nunca são apagados.
/// </summary>
public sealed partial class SaleReturnService(
    ISalesDb db,
    IPosReturnSupport pos,
    ISaleCustomerDocuments documents,
    IAuditWriter audit,
    IStockMovements movements,
    IItems items,
    IItemCosts costs,
    IPaymentMethods paymentMethods,
    INumberSequence numbers,
    IEventPublisher events,
    TimeProvider clock)
{
    public const string RateLimitPolicy = "pos-returns-lookup";
    public const int MaxCandidates = 20;
    public const string NotRegisterMessage = "Devolução só no caixa registrado, com rede.";
    public const string NotFoundMessage = "Nenhuma venda encontrada com este número ou CPF.";

    /// <summary>CA 1: a venda pelo número (exato) ou pelo documento (índice cego, leitura auditada); só vendas feitas do balcão.</summary>
    public async Task<Result<SaleLookupResult>> LookupAsync(string? posCookie, SaleLookupInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await pos.ContextAsync(posCookie, cancellationToken) is not { } context)
        {
            return Error.Forbidden(Error.Codes.Forbidden, NotRegisterMessage);
        }

        var number = input.Number?.Trim().ToUpperInvariant();
        var document = input.Document?.Trim();
        if (string.IsNullOrEmpty(number) == string.IsNullOrEmpty(document) || number?.Length > 40 || document?.Length > 20)
        {
            return Field("number", "Informe o número do cupom ou o CPF do cliente.");
        }

        List<Guid> ids;
        if (!string.IsNullOrEmpty(number))
        {
            ids = await db.Sales.AsNoTracking().Where(s => s.Channel == SaleChannel.Pos && s.Number == number).Select(s => s.Id).ToListAsync(cancellationToken);
        }
        else
        {
            ids = (await documents.FindSalesAsync(document!, cancellationToken)).Take(200).ToList();
            // PP-26: a leitura pelo CPF fica auditada (quem, quando, de qual IP), sem o documento; uma linha por venda achada.
            foreach (var id in ids.Take(MaxCandidates))
            {
                await audit.RecordReadAsync(nameof(SaleCustomerDocument), id.ToString(), "Localizar devolução", cancellationToken);
            }
        }

        var sales = await db.Sales.AsNoTracking().Where(s => ids.Contains(s.Id) && s.Channel == SaleChannel.Pos && ReturnRules.Done.Contains(s.Status))
            .OrderByDescending(s => s.CompletedAt).Take(MaxCandidates).ToListAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        var candidates = new List<ReturnCandidate>();
        foreach (var sale in sales)
        {
            candidates.Add(await CandidateAsync(sale, context.ExchangeDays, today, cancellationToken));
        }

        return new SaleLookupResult(candidates, context.CashSessionId is not null);
    }

    /// <summary>CA 2 a 5 e 9: pede a devolução. Fora do prazo ou em dinheiro, a conclusão pede o supervisor.</summary>
    public async Task<Result<SaleReturnView>> RequestAsync(string? posCookie, Guid operatorId, ReturnRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await pos.ContextAsync(posCookie, cancellationToken) is not { } context)
        {
            return Error.Forbidden(Error.Codes.Forbidden, NotRegisterMessage);
        }

        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.SaleId && s.Channel == SaleChannel.Pos, cancellationToken);
        if (sale is null || !ReturnRules.Done.Contains(sale.Status))
        {
            return Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
        }

        if (!Enum.IsDefined(input.RefundKind))
        {
            return Field("refundKind", "Escolha a forma do reembolso.");
        }

        if (input.Exchange && input.RefundKind != RefundKind.StoreCredit)
        {
            return Field("refundKind", "Na troca, a devolução vira vale para pagar a venda nova.");
        }

        var note = Clean(input.Note);
        if (note?.Length > SaleReturn.MaxNoteLength || SaleRules.LooksLikeCardNumber(note))
        {
            return Field("note", $"Observação de até {SaleReturn.MaxNoteLength} caracteres, sem número de cartão.");
        }

        var built = await BuildLinesAsync(sale, input.Lines ?? [], cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var (lines, total) = built.Value;
        string? nsu = null;
        if (input.RefundKind == RefundKind.CardReversal)
        {
            var card = await CardCheckAsync(sale.Id, input.CardPaymentId, input.CardNsu, total, cancellationToken);
            if (card.IsFailure)
            {
                return card.Error!;
            }

            nsu = card.Value;
        }

        if (input.RefundKind == RefundKind.Cash && context.CashSessionId is null)
        {
            return Error.Validation("cash_session_closed", "Abra o caixa para devolver em dinheiro, ou escolha o vale-troca.");
        }

        var today = BusinessTime.Today(clock);
        var deadline = ReturnRules.Deadline(sale.BusinessDate, context.ExchangeDays);
        var id = Guid.CreateVersion7();
        var request = SaleReturn.Request(id, await numbers.NextAsync(SaleReturn.DocType, cancellationToken), sale.Id, input.RefundKind, total, context.StoreId, context.RegisterId,
            context.DeviceId, context.CashSessionId, operatorId, ReturnRules.IsLate(today, deadline), deadline, input.RefundKind == RefundKind.CardReversal ? input.CardPaymentId : null,
            nsu, input.Exchange, input.CustomerId ?? sale.CustomerId, note, clock.GetUtcNow());
        db.SaleReturns.Add(request);
        db.SaleReturnLines.AddRange(lines.Select(l => SaleReturnLine.Create(id, l.Line, l.Quantity, l.Amount, l.Destination, l.Reason)));
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(id, null, cancellationToken) is { } view ? view : Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
    }

    /// <summary>
    /// Aprovar e concluir (CA 3, 5 e 6): no mesmo caixa do pedido; com o supervisor quando pede; quantidades conferidas de novo sob a trava da venda; o
    /// dinheiro não passa do que há na gaveta. Tudo na mesma transação; o <see cref="SaleReturned"/> vai no outbox.
    /// </summary>
    public async Task<Result<SaleReturnView>> ApproveAsync(string? posCookie, Guid operatorId, Guid returnId, ReturnApproveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await pos.ContextAsync(posCookie, cancellationToken) is not { } context)
        {
            return Error.Forbidden(Error.Codes.Forbidden, NotRegisterMessage);
        }

        // IDOR: a devolução só é vista e concluída no caixa em que foi pedida.
        var key = await db.SaleReturns.AsNoTracking().Where(r => r.Id == returnId && r.RegisterId == context.RegisterId).Select(r => (Guid?)r.SaleId).FirstOrDefaultAsync(cancellationToken);
        if (key is not { } saleId)
        {
            return Error.NotFound(Error.Codes.NotFound, "Devolução não encontrada neste caixa.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Uma conclusão por venda de cada vez: duas devoluções da mesma venda não passam juntas do que pode voltar.
        await LockAsync("sales.return:" + saleId.ToString("N"), cancellationToken);
        var request = await db.SaleReturns.FirstAsync(r => r.Id == returnId, cancellationToken);

        if (request.Status == SaleReturnStatus.Completed)
        {
            return await ViewAsync(request.Id, null, cancellationToken) is { } done ? done : Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
        }

        if (request.Status != SaleReturnStatus.Requested)
        {
            return Error.Validation("return_not_requested", "Esta devolução não está mais pedida.");
        }

        var sale = await db.Sales.FirstAsync(s => s.Id == request.SaleId, cancellationToken);
        if (!ReturnRules.Done.Contains(sale.Status))
        {
            return Error.Validation("sale_not_returnable", "A venda foi cancelada: não há o que devolver.");
        }

        var lines = await db.SaleReturnLines.Where(l => l.ReturnId == request.Id).ToListAsync(cancellationToken);
        var saleLines = await db.SaleLines.Where(l => l.SaleId == sale.Id).ToListAsync(cancellationToken);
        var returned = await ReturnedAsync(sale.Id, cancellationToken);
        foreach (var line in lines)
        {
            var saleLine = saleLines.First(l => l.Id == line.SaleLineId);
            if (line.Quantity > ReturnRules.Returnable(saleLine.Quantity, returned.GetValueOrDefault(saleLine.Id).Quantity))
            {
                return Field("lines", $"{saleLine.Description}: a quantidade passa do que ainda pode voltar.");
            }
        }

        // CA 3 e 5: fora do prazo ou em dinheiro, só com a aprovação do supervisor (PIN no caixa, PDV-04), presa a esta devolução e ao valor dela.
        Guid approvedBy = operatorId;
        if (request.NeedsSupervisor)
        {
            if (input.SupervisorApprovalId is not { } approvalId)
            {
                return Error.Validation("supervisor_required", request.Late ? ReturnRules.LateMessage(request.Deadline) + " Chame o supervisor." : "Devolução em dinheiro pede o supervisor.");
            }

            var used = await pos.UseApprovalAsync(approvalId, operatorId, context.RegisterId, request.Id, request.Total, cancellationToken);
            if (used.IsFailure)
            {
                return used.Error!;
            }

            approvedBy = used.Value;
        }

        if (request.RefundKind == RefundKind.Cash)
        {
            if (context.CashSessionId is not { } session || session != request.CashSessionId)
            {
                return Error.Validation("cash_session_changed", "A sessão de caixa mudou desde o pedido. Peça a devolução de novo.");
            }

            if (request.Total > context.CashInDrawer)
            {
                return Error.Validation("cash_not_enough", "O valor passa do dinheiro em caixa. Escolha o vale-troca ou faça um suprimento.");
            }
        }

        if (request.RefundKind == RefundKind.CardReversal)
        {
            var card = await CardCheckAsync(sale.Id, request.CardPaymentId, request.CardNsu, request.Total, cancellationToken);
            if (card.IsFailure)
            {
                return card.Error!;
            }
        }

        var now = clock.GetUtcNow();
        var businessDate = BusinessTime.Today(clock);
        var stock = await PostStockAsync(request, lines, saleLines, context.StockLocationId, now, cancellationToken);
        if (stock is not null)
        {
            return stock;
        }

        StoreCredit? credit = null;
        string? code = null;
        if (request.RefundKind == RefundKind.StoreCredit)
        {
            code = ReturnRules.NewCode();
            var normalized = ReturnRules.Normalize(code)!;
            credit = StoreCredit.Issue(ReturnRules.Hash(normalized), normalized[^4..], request.CustomerId, request.Total, businessDate.AddDays(context.StoreCreditValidityDays),
                request.Id, context.StoreId, now);
            db.StoreCredits.Add(credit);
        }
        else if (request.RefundKind == RefundKind.Cash)
        {
            await pos.RecordCashRefundAsync(request.CashSessionId!.Value, request.Id, request.Number, request.Total, operatorId, now, cancellationToken);
        }

        request.Complete(approvedBy, input.SupervisorApprovalId, credit?.Id, now, businessDate);
        var after = saleLines.Select(l => (l.Quantity, returned.GetValueOrDefault(l.Id).Quantity + lines.Where(r => r.SaleLineId == l.Id).Sum(r => r.Quantity)));
        sale.MarkReturned(ReturnRules.StatusAfter(after));
        Guid? cardMethod = request.CardPaymentId is { } paymentId
            ? await db.SalePayments.AsNoTracking().Where(p => p.Id == paymentId).Select(p => (Guid?)p.PaymentMethodId).FirstOrDefaultAsync(cancellationToken)
            : null;
        events.Publish(new SaleReturned(request.Id, request.Number, sale.Id, request.StoreId, request.CashSessionId, sale.CashSessionId, request.RefundKind, request.Total, credit?.Id,
            cardMethod, businessDate, now));
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return await ViewAsync(request.Id, code, cancellationToken) is { } view ? view : Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
    }

    /// <summary>
    /// CA 4 e 6: "Volta ao estoque" entra no local da loja (entrada de devolução pelo custo da venda); "Avariado" entra e sai como perda, com o motivo.
    /// Serviço não mexe; item com lote ou série fica com a nota para o estoquista lançar.
    /// </summary>
    private async Task<Error?> PostStockAsync(SaleReturn request, List<SaleReturnLine> lines, List<SaleLine> saleLines, Guid locationId, DateTimeOffset now, CancellationToken ct)
    {
        var itemIds = lines.Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).Distinct().ToList();
        var info = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id);
        var averages = await costs.AverageCostsAsync(itemIds, ct);
        var entries = new List<(SaleReturnLine Line, StockPosting Posting)>();
        var losses = new List<(SaleReturnLine Line, StockPosting Posting)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.ItemId is not { } itemId || info.GetValueOrDefault(itemId) is not { } item || item.Type == ItemType.Service)
            {
                line.StockNote = "Serviço: sem estoque.";
                continue;
            }

            if (item.TracksLot || item.TracksSerial)
            {
                line.StockNote = "Item com controle de lote ou série: lance a devolução pelo estoque.";
                continue;
            }

            var cost = saleLines.First(l => l.Id == line.SaleLineId).UnitCost ?? averages.GetValueOrDefault(itemId);
            var reason = $"Devolução {request.Number}: {line.Reason}";
            var leg = (i * 2) + 1;
            // A entrada de devolução leva o custo da venda; sem custo conhecido, entra como ajuste pelo custo médio (o motor exige custo na devolução).
            entries.Add((line, cost > 0
                ? new StockPosting(StockMovementType.Return, itemId, locationId, null, line.Quantity, Math.Round(cost, 6), StockSources.SaleReturn, request.Id, leg, At: now,
                    Reason: Cut(reason, 200), SourceNumber: request.Number)
                : new StockPosting(StockMovementType.Adjustment, itemId, locationId, null, line.Quantity, null, StockSources.SaleReturn, request.Id, leg, At: now,
                    Reason: Cut(reason, 200), SourceNumber: request.Number)));
            if (line.Destination == ReturnDestination.Damaged)
            {
                losses.Add((line, new StockPosting(StockMovementType.Adjustment, itemId, locationId, null, -line.Quantity, null, StockSources.SaleReturn, request.Id, leg + 1, At: now,
                    Reason: Cut($"Perda (avaria) da devolução {request.Number}: {line.Reason}", 200), SourceNumber: request.Number)));
            }
        }

        // Primeiro as entradas, depois as perdas: a perda sai do que acabou de entrar.
        return await PostBatchAsync(entries, (l, m) => l.StockMovementId = m, ct) ?? await PostBatchAsync(losses, (l, m) => l.LossMovementId = m, ct);
    }

    private async Task<Error?> PostBatchAsync(List<(SaleReturnLine Line, StockPosting Posting)> batch, Action<SaleReturnLine, Guid> setter, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return null;
        }

        var posted = await movements.PostManyAsync(batch.Select(b => b.Posting).ToList(), ct);
        if (posted.IsFailure)
        {
            return posted.Error!;
        }

        for (var i = 0; i < batch.Count; i++)
        {
            setter(batch[i].Line, posted.Value[i].MovementId);
        }

        return null;
    }

    /// <summary>Linhas pedidas: da venda, sem repetir, quantidade até o que pode voltar (CA 2), destino e motivo (CA 4); o valor é o praticado (rateado).</summary>
    private async Task<Result<(List<(SaleLine Line, decimal Quantity, decimal Amount, ReturnDestination Destination, string Reason)> Lines, decimal Total)>> BuildLinesAsync(
        Sale sale, IReadOnlyList<SaleReturnLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0 || inputs.Count > SaleRules.MaxLines || inputs.Select(l => l.SaleLineId).Distinct().Count() != inputs.Count)
        {
            return Field("lines", "Escolha os itens que voltam, cada um uma vez.");
        }

        var saleLines = await db.SaleLines.AsNoTracking().Where(l => l.SaleId == sale.Id).ToDictionaryAsync(l => l.Id, ct);
        var returned = await ReturnedAsync(sale.Id, ct);
        var result = new List<(SaleLine, decimal, decimal, ReturnDestination, string)>();
        foreach (var input in inputs)
        {
            if (!saleLines.TryGetValue(input.SaleLineId, out var line))
            {
                return Field("lines", "Item que não é desta venda.");
            }

            var reason = input.Reason?.Trim() ?? string.Empty;
            if (!Enum.IsDefined(input.Destination) || reason.Length is < 3 or > SaleReturnLine.MaxReasonLength || SaleRules.LooksLikeCardNumber(reason))
            {
                return Field("lines", $"{line.Description}: escolha o destino e informe o motivo (de 3 a {SaleReturnLine.MaxReasonLength} caracteres).");
            }

            var quantity = SaleRules.Quantity(input.Quantity);
            var already = returned.GetValueOrDefault(line.Id);
            var returnable = ReturnRules.Returnable(line.Quantity, already.Quantity);
            if (quantity <= 0 || quantity != input.Quantity || quantity > returnable)
            {
                return Field("lines", $"{line.Description}: devolva até {returnable.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} (vendido menos o já devolvido).");
            }

            var amount = ReturnRules.Amount(line.Total, line.Quantity, quantity, already.Quantity, already.Amount);
            result.Add((line, quantity, amount, input.Destination, reason));
        }

        var total = SaleRules.Money(result.Sum(r => r.Item3));
        return total <= 0 ? Field("lines", "A devolução precisa de valor maior que zero.") : (result, total);
    }

    /// <summary>CA 5: o pagamento é um cartão capturado desta venda, o valor cabe no que falta estornar e o NSU do estorno vem do comprovante.</summary>
    private async Task<Result<string>> CardCheckAsync(Guid saleId, Guid? paymentId, string? nsu, decimal amount, CancellationToken ct)
    {
        var clean = nsu?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > SaleReturn.MaxNsuLength || !clean.All(char.IsAsciiLetterOrDigit) || SaleRules.LooksLikeCardNumber(clean))
        {
            return Field("cardNsu", "Estorne na maquininha e informe o NSU do estorno (até 12 letras ou números).");
        }

        var cards = await CardPaymentsAsync(saleId, ct);
        if (paymentId is not { } id || cards.FirstOrDefault(c => c.PaymentId == id) is not { } card)
        {
            return Field("cardPaymentId", "Escolha o pagamento no cartão desta venda.");
        }

        return amount > card.Reversible ? Field("cardPaymentId", "O valor passa do que ainda pode ser estornado neste cartão.") : clean;
    }

    /// <summary>Pagamentos de cartão capturados da venda e o que cada um ainda pode estornar (valor − estornos de devoluções concluídas).</summary>
    private async Task<List<ReturnCandidatePayment>> CardPaymentsAsync(Guid saleId, CancellationToken ct)
    {
        var cardTypes = (await paymentMethods.ActiveAsync(BusinessTime.Today(clock), ct))
            .Where(m => m.Type is PaymentMethodType.Debit or PaymentMethodType.Credit or PaymentMethodType.CreditInstallments or PaymentMethodType.Voucher).Select(m => m.Id).ToHashSet();
        var payments = await db.SalePayments.AsNoTracking().Where(p => p.SaleId == saleId && p.Status == SalePaymentStatus.Captured).ToListAsync(ct);
        var reversed = await db.SaleReturns.AsNoTracking().Where(r => r.SaleId == saleId && r.Status == SaleReturnStatus.Completed && r.CardPaymentId != null)
            .GroupBy(r => r.CardPaymentId!.Value).Select(g => new { g.Key, Amount = g.Sum(r => r.Total) }).ToDictionaryAsync(g => g.Key, g => g.Amount, ct);
        return payments.Where(p => cardTypes.Contains(p.PaymentMethodId) || p.CardBrand is not null)
            .Select(p => new ReturnCandidatePayment(p.Id, p.PaymentMethodId, p.CardBrand, p.NetAmount, Math.Max(0m, p.NetAmount - reversed.GetValueOrDefault(p.Id)))).ToList();
    }

    /// <summary>Quantidade e valor já devolvidos por linha da venda (só devoluções concluídas).</summary>
    private async Task<Dictionary<Guid, (decimal Quantity, decimal Amount)>> ReturnedAsync(Guid saleId, CancellationToken ct) =>
        (await db.SaleReturnLines.AsNoTracking()
            .Join(db.SaleReturns.AsNoTracking().Where(r => r.SaleId == saleId && r.Status == SaleReturnStatus.Completed), l => l.ReturnId, r => r.Id, (l, r) => l)
            .GroupBy(l => l.SaleLineId).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity), Amount = g.Sum(l => l.Amount) })
            .ToListAsync(ct))
        .ToDictionary(g => g.Key, g => (g.Quantity, g.Amount));

    private async Task<ReturnCandidate> CandidateAsync(Sale sale, int exchangeDays, DateOnly today, CancellationToken ct)
    {
        var lines = await db.SaleLines.AsNoTracking().Where(l => l.SaleId == sale.Id).OrderBy(l => l.LineNumber).ToListAsync(ct);
        var returned = await ReturnedAsync(sale.Id, ct);
        var deadline = ReturnRules.Deadline(sale.BusinessDate, exchangeDays);
        var late = ReturnRules.IsLate(today, deadline);
        return new ReturnCandidate(sale.Id, sale.Number, sale.CompletedAt, sale.BusinessDate, sale.Total, sale.Status, deadline, late, late ? ReturnRules.LateMessage(deadline) : null,
            lines.Select(l =>
            {
                var already = returned.GetValueOrDefault(l.Id);
                return new ReturnCandidateLine(l.Id, l.Description, l.UnitCode, l.Quantity, already.Quantity, ReturnRules.Returnable(l.Quantity, already.Quantity), l.Total,
                    l.Quantity > 0 ? SaleRules.Money(l.Total / l.Quantity) : 0m);
            }).ToList(),
            await CardPaymentsAsync(sale.Id, ct));
    }

    private async Task<SaleReturnView?> ViewAsync(Guid id, string? code, CancellationToken ct)
    {
        var request = await db.SaleReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return null;
        }

        var saleNumber = await db.Sales.AsNoTracking().Where(s => s.Id == request.SaleId).Select(s => s.Number).FirstAsync(ct);
        var lines = await db.SaleReturnLines.AsNoTracking().Where(l => l.ReturnId == id).OrderBy(l => l.Id).ToListAsync(ct);
        var credit = request.StoreCreditId is { } creditId ? await db.StoreCredits.AsNoTracking().FirstOrDefaultAsync(c => c.Id == creditId, ct) : null;
        return new SaleReturnView(request.Id, request.Number, request.SaleId, saleNumber, request.Status, request.RefundKind, request.Total, request.Late, request.Deadline,
            request.Late ? ReturnRules.LateMessage(request.Deadline) : null, request.NeedsSupervisor, request.IsExchange, request.CardNsu,
            lines.Select(l => new SaleReturnLineView(l.Id, l.SaleLineId, l.Description, l.Quantity, l.Amount, l.Destination, l.Reason, l.StockNote)).ToList(),
            credit is null ? null : new StoreCreditIssued(credit.Id, code, credit.CodeLast4, credit.Amount, credit.Balance, credit.ExpiresOn), request.RequestedAt, request.CompletedAt);
    }

    private async Task LockAsync(string key, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(ct);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    internal static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
