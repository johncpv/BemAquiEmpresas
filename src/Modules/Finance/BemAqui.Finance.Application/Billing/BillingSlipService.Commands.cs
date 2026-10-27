using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BillingSlipService
{
    /// <summary>
    /// "Emitir boleto" (CA 4 a 11, 15, 22): valida, confere os limites (acima, só com <c>finance.approve</c>), e numa transação grava o
    /// boleto <c>Requested</c> com o "seu número" da sequência da conta, liga o título (a gravação condicional pelo xmin barra a segunda
    /// emissão simultânea, e o índice único parcial também) e enfileira o <see cref="IssueBillingSlipJob"/> depois do commit.
    /// </summary>
    public async Task<Result<BillingSlipResult>> IssueAsync(Guid receivableId, BillingSlipInput input, Guid? clientRequestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable is null)
        {
            return ReceivableService.NotFound();
        }

        // CA 9: o mesmo Idempotency-Key devolve o boleto original.
        if (clientRequestId is { } key && await db.BillingSlips.AsNoTracking().FirstOrDefaultAsync(s => s.ClientRequestId == key && s.ReceivableId == receivableId, cancellationToken) is { } replay)
        {
            return new BillingSlipResult(await ViewAsync(replay, cancellationToken), RequestedMessage);
        }

        if (receivable.Version != input.Version)
        {
            return Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O título mudou desde que você abriu. Recarregue.");
        }

        var checkedTerms = await CheckAsync(receivable, input.BankAccountId, input.DueDate, input.FinePct, input.InterestMonthPct, input.DiscountAmount, input.DiscountUntil, input.Message, cancellationToken);
        if (checkedTerms.IsFailure)
        {
            return checkedTerms.Error!;
        }

        var (terms, integration, approved, fineFrom) = checkedTerms.Value;
        var created = await CreateAsync(receivable, integration, terms, fineFrom, approved, input.SendEmail, clientRequestId, cancellationToken);
        return created.IsFailure ? created.Error! : new BillingSlipResult(await ViewAsync(created.Value, cancellationToken), RequestedMessage);
    }

    /// <summary>Validação comum ao diálogo e ao lote: título, boleto aberto, integração, pré-condições, vencimento, valores e limites.</summary>
    private async Task<Result<(BillingSlipTerms Terms, BankIntegration Integration, bool Approved, DateOnly FineFrom)>> CheckAsync(
        Receivable receivable, Guid bankAccountId, DateOnly dueDate, decimal finePct, decimal interestPct, decimal discount, DateOnly? discountUntil, string? message, CancellationToken ct)
    {
        if (!BillingSlipRules.Eligible(receivable))
        {
            return Error.Conflict("receivable_not_eligible", "Só título em aberto, com saldo e vencimento, recebe boleto.");
        }

        if (await ExistingErrorAsync(receivable, ct) is { } exists)
        {
            return exists;
        }

        var allowed = await integrations.EffectsAllowedAsync(bankAccountId, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error!.Type == ErrorType.NotFound ? Error.Conflict(MissingIntegrationCode, NoIntegrationMessage) : allowed.Error!;
        }

        var integration = allowed.Value;
        if (integration.LastTestOutcome != BankTestOutcome.Ok)
        {
            return Error.Conflict(MissingIntegrationCode, "Teste a conexão com o Inter desta conta antes de emitir boletos.");
        }

        var party = await parties.FindAsync(receivable.PartyId, ct);
        var accounts = new[] { new BillingAccountOption(integration.BankAccountId, string.Empty, false, true) };
        var (preconditions, _) = await PreconditionsAsync(receivable, party, accounts, ct);
        if (preconditions.FirstOrDefault(p => p.Blocking) is { } missing)
        {
            return Error.Validation(PreconditionsCode, missing.Text, new Dictionary<string, string[]> { ["partyId"] = [missing.Text] });
        }

        var today = Today;
        if (dueDate < today)
        {
            return ReceivableService.Invalid("dueDate", "O vencimento do boleto não pode ser antes de hoje.");
        }

        if (dueDate > today.AddYears(1))
        {
            return ReceivableService.Invalid("dueDate", "O vencimento do boleto pode ser de até 1 ano.");
        }

        if (finePct < 0 || finePct > BillingSlipRules.MaxFinePct)
        {
            return ReceivableService.Invalid("finePct", "A multa deve ficar entre 0,00% e 2,00%.");
        }

        if (interestPct < 0 || interestPct > BillingSlipRules.MaxInterestMonthPct)
        {
            return ReceivableService.Invalid("interestMonthPct", "Os juros de mora devem ficar entre 0,00% e 1,00% ao mês.");
        }

        var amount = BillingSlipRules.AmountFor(receivable);
        if (discount < 0 || discount >= amount)
        {
            return ReceivableService.Invalid("discountAmount", "O desconto deve ser maior ou igual a zero e menor que o valor do boleto.");
        }

        if (discount > 0 && (discountUntil is not { } until || until > dueDate || until < today))
        {
            return ReceivableService.Invalid("discountUntil", "Informe até quando vale o desconto: de hoje até o vencimento do boleto.");
        }

        if (!BillingSlipRules.ValidMessage(message))
        {
            return ReceivableService.Invalid("message", "A mensagem ao pagador tem até 3 linhas de até 78 caracteres.");
        }

        var lines = BillingSlipRules.MessageLines(message);
        var terms = new BillingSlipTerms(amount, dueDate, Math.Round(finePct, 2), Math.Round(interestPct, 2), Math.Round(discount, 2), discount > 0 ? discountUntil : null,
            lines.Count > 0 ? string.Join('\n', lines) : null);
        var defaults = await settings.DefaultsAsync(ct);
        var limits = await parameters.LimitsAsync(ct);
        var approved = false;
        if (BillingSlipRules.RequiresApproval(terms, new DiscountLimits(limits.DiscountApprovalPct, limits.DiscountApprovalAmount), defaults))
        {
            if (!await permissions.HasPermissionAsync("finance", "approve", ct))
            {
                return Error.Forbidden(ApprovalCode, ApprovalMessage);
            }

            approved = true;
        }

        var fineFrom = BillingSlipRules.FineFrom(receivable.DueDate!.Value, dueDate, defaults.OverdueFineFrom);
        return (terms, integration, approved, fineFrom);
    }

    private async Task<Error?> ExistingErrorAsync(Receivable receivable, CancellationToken ct)
    {
        var holding = await db.BillingSlips.AsNoTracking()
            .Where(s => s.ReceivableId == receivable.Id && (s.Status == BillingSlipStatus.Requested || s.Status == BillingSlipStatus.Issued || s.Status == BillingSlipStatus.Failed))
            .Select(s => (BillingSlipStatus?)s.Status).FirstOrDefaultAsync(ct);
        return holding switch
        {
            BillingSlipStatus.Failed => Error.Conflict(FailedCode, "A última emissão deste título falhou. Use Tentar de novo ou cancele o boleto."),
            not null => Error.Conflict(ExistsCode, "Este título já tem um boleto em aberto. Cancele o atual para emitir outro."),
            _ => null,
        };
    }

    private async Task<Result<BillingSlip>> CreateAsync(Receivable receivable, BankIntegration integration, BillingSlipTerms terms, DateOnly fineFrom, bool approved, bool sendEmail, Guid? clientRequestId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var next = await NextReferenceAsync(integration.BankAccountId, ct);
        var slip = new BillingSlip
        {
            ReceivableId = receivable.Id, BankAccountId = integration.BankAccountId, Provider = integration.Provider, OurReference = BillingReference.ToBase36(next),
            Amount = terms.Amount, DueDate = terms.DueDate, ReceivableDueDate = receivable.DueDate!.Value, FineFrom = fineFrom, FinePct = terms.FinePct,
            InterestMonthPct = terms.InterestMonthPct, DiscountAmount = terms.DiscountAmount, DiscountUntil = terms.DiscountUntil, Message = terms.Message,
            ApprovedBy = approved ? currentUser.UserId : null, ClientRequestId = clientRequestId, SendOnIssue = sendEmail, RequestedAt = clock.GetUtcNow(), RequestedBy = currentUser.UserId,
        };
        db.BillingSlips.Add(slip);
        // Transição condicional do título (CA 22): a versão lida vale como condição; outra emissão no meio vira 409.
        receivable.BillingSlipId = slip.Id;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            Forget(slip, receivable);
            return Error.Conflict(ExistsCode, "Este título já tem um boleto em aberto. Cancele o atual para emitir outro.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ActiveIndex) || DbErrors.IsUniqueViolation(ex, RequestIndex))
        {
            Forget(slip, receivable);
            return Error.Conflict(ExistsCode, "Este título já tem um boleto em aberto. Cancele o atual para emitir outro.");
        }

        await jobs.EnqueueIssueAsync(slip.Id, ct);
        await transaction.CommitAsync(ct);
        return slip;
    }

    private void Forget(BillingSlip slip, Receivable receivable)
    {
        db.Entry(slip).State = EntityState.Detached;
        db.Entry(receivable).State = EntityState.Detached;
    }

    /// <summary>Próximo valor da sequência do "seu número" da conta, na transação do pedido (CA 6).</summary>
    private async Task<long> NextReferenceAsync(Guid bankAccountId, CancellationToken ct)
    {
        var values = await db.Database.SqlQuery<long>(
            $"INSERT INTO finance.billing_reference_sequences (bank_account_id, last_value) VALUES ({bankAccountId}, 1) ON CONFLICT (bank_account_id) DO UPDATE SET last_value = finance.billing_reference_sequences.last_value + 1 RETURNING last_value AS \"Value\"")
            .ToListAsync(ct);
        return values[0];
    }

    /// <summary>Prévia do lote (CA 19): elegíveis com título, cliente, valor, vencimento e e-mail; recusados com o motivo; total.</summary>
    public async Task<Result<BillingBatchPreview>> BatchPreviewAsync(BillingBatchPreviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ids = input.ReceivableIds?.Distinct().ToList() ?? [];
        if (ids.Count is 0 or > BillingSlipRules.MaxBatch)
        {
            return ReceivableService.Invalid("receivableIds", "Selecione de 1 a 50 títulos.");
        }

        var accounts = await AccountsAsync(cancellationToken);
        var account = accounts.FirstOrDefault();
        var receivables = await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(receivables.Select(r => r.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var eligible = new List<BillingBatchItem>();
        var refused = new List<BillingBatchRefusal>();
        foreach (var receivable in receivables.OrderBy(r => r.DueDate).ThenBy(r => r.Number, StringComparer.Ordinal))
        {
            var party = names.GetValueOrDefault(receivable.PartyId);
            var name = party is null ? string.Empty : party.TradeName ?? party.LegalName;
            var (preconditions, email) = await PreconditionsAsync(receivable, party, accounts, cancellationToken);
            if (preconditions.FirstOrDefault(p => p.Blocking) is { } blocking)
            {
                refused.Add(new BillingBatchRefusal(receivable.Id, receivable.Number, name, blocking.Text));
                continue;
            }

            eligible.Add(new BillingBatchItem(receivable.Id, receivable.Number, name, BillingSlipRules.AmountFor(receivable), BillingSlipRules.SuggestedDueDate(receivable.DueDate, Today), email));
        }

        return new BillingBatchPreview(eligible, refused, eligible.Sum(e => e.Amount), account?.Id, account?.Sandbox ?? false);
    }

    /// <summary>
    /// "Emitir {n} boletos" (CA 19): cada título vira um boleto com o seu <c>Idempotency-Key</c>, pelos padrões de Configurações →
    /// Cobrança (vencido: o próximo dia útil); quem não passa volta com o motivo.
    /// </summary>
    public async Task<Result<BillingBatchResult>> IssueBatchAsync(BillingBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var items = input.Items?.DistinctBy(i => i.ReceivableId).ToList() ?? [];
        if (items.Count is 0 or > BillingSlipRules.MaxBatch || items.Any(i => i.ClientRequestId == Guid.Empty))
        {
            return ReceivableService.Invalid("items", "Selecione de 1 a 50 títulos, cada um com a sua chave.");
        }

        var account = (await AccountsAsync(cancellationToken)).FirstOrDefault();
        if (account is null)
        {
            return Error.Conflict(MissingIntegrationCode, NoIntegrationMessage);
        }

        var defaults = await settings.DefaultsAsync(cancellationToken);
        var slipIds = new List<Guid>();
        var refused = new List<BillingBatchRefusal>();
        foreach (var item in items)
        {
            var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == item.ReceivableId, cancellationToken);
            if (receivable is null)
            {
                refused.Add(new BillingBatchRefusal(item.ReceivableId, string.Empty, string.Empty, "Título a receber não encontrado."));
                continue;
            }

            if (await db.BillingSlips.AsNoTracking().FirstOrDefaultAsync(s => s.ClientRequestId == item.ClientRequestId && s.ReceivableId == receivable.Id, cancellationToken) is { } replay)
            {
                slipIds.Add(replay.Id);
                continue;
            }

            var amount = BillingSlipRules.AmountFor(receivable);
            var due = BillingSlipRules.SuggestedDueDate(receivable.DueDate, Today);
            var discount = BillingSlipRules.DefaultDiscount(amount, defaults.DefaultDiscountPct);
            var checkedTerms = await CheckAsync(receivable, account.Id, due, defaults.FinePct, defaults.InterestMonthPct, discount, discount > 0 ? due : null, null, cancellationToken);
            if (checkedTerms.IsFailure)
            {
                refused.Add(new BillingBatchRefusal(receivable.Id, receivable.Number, string.Empty, checkedTerms.Error!.Message));
                db.Entry(receivable).State = EntityState.Detached;
                continue;
            }

            var (terms, integration, approved, fineFrom) = checkedTerms.Value;
            var created = await CreateAsync(receivable, integration, terms, fineFrom, approved, input.SendEmail, item.ClientRequestId, cancellationToken);
            if (created.IsFailure)
            {
                refused.Add(new BillingBatchRefusal(receivable.Id, receivable.Number, string.Empty, created.Error!.Message));
                continue;
            }

            slipIds.Add(created.Value.Id);
        }

        var n = slipIds.Count;
        return new BillingBatchResult(n, slipIds, refused, n == 1 ? "1 boleto solicitado" : $"{n} boletos solicitados");
    }

    /// <summary>"Tentar de novo" (CA 8): o mesmo boleto e o mesmo "seu número"; o job consulta o banco antes de criar outra cobrança.</summary>
    public async Task<Result<BillingSlipResult>> RetryAsync(Guid receivableId, Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId && s.ReceivableId == receivableId, cancellationToken);
        if (slip is null)
        {
            return NotFound();
        }

        if (slip.Status != BillingSlipStatus.Failed || slip.CancelPending)
        {
            return Error.Conflict("billing_slip_not_failed", "Só o boleto com falha na emissão pode ser reenviado.");
        }

        var receivable = await db.Receivables.AsNoTracking().FirstAsync(r => r.Id == receivableId, cancellationToken);
        if (!BillingSlipRules.Eligible(receivable))
        {
            return Error.Conflict("receivable_not_eligible", "Só título em aberto, com saldo e vencimento, recebe boleto.");
        }

        var allowed = await integrations.EffectsAllowedAsync(slip.BankAccountId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error!.Type == ErrorType.NotFound ? Error.Conflict(MissingIntegrationCode, NoIntegrationMessage) : allowed.Error!;
        }

        slip.Retry();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O boleto mudou desde que você abriu. Recarregue.");
        }

        await jobs.EnqueueIssueAsync(slip.Id, cancellationToken);
        return new BillingSlipResult(await ViewAsync(slip, cancellationToken), "Emissão reenviada");
    }

    /// <summary>
    /// "Cancelar boleto" (CA 16): motivo da lista; o pedido vai ao Inter pelo job e o boleto fica <c>Cancelled</c> depois da
    /// confirmação do banco. Boleto com falha também se cancela (o job consulta o banco antes, porque a cobrança pode existir).
    /// </summary>
    public async Task<Result<BillingSlipResult>> CancelAsync(Guid receivableId, Guid slipId, BillingSlipCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId && s.ReceivableId == receivableId, cancellationToken);
        if (slip is null)
        {
            return NotFound();
        }

        if (!Enum.IsDefined(input.Reason) || !BillingSlipRules.IsManual(input.Reason))
        {
            return ReceivableService.Invalid("reason", "Escolha o motivo do cancelamento na lista.");
        }

        if (!slip.HoldsReceivable)
        {
            return Error.Conflict("billing_slip_not_open", "Este boleto não está em aberto.");
        }

        if (slip.CancelPending)
        {
            return Error.Conflict("billing_slip_cancel_pending", "O cancelamento deste boleto já foi enviado ao Inter.");
        }

        slip.RequestCancel(input.Reason, currentUser.UserId, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O boleto mudou desde que você abriu. Recarregue.");
        }

        await jobs.EnqueueCancelAsync(slip.Id, cancellationToken);
        return new BillingSlipResult(await ViewAsync(slip, cancellationToken), "Cancelamento enviado ao Inter. Assim que o banco confirmar, o boleto aparece cancelado.");
    }

    /// <summary>
    /// "Enviar ao cliente" (CA 13): o PDF anexado (só sai depois do <c>Clean</c>), para o contato de cobrança e até 3 cópias, com o
    /// Reply-To do usuário; acima do limite diário de Configurações → Cobrança, o envio fica para o dia seguinte às 08:00.
    /// </summary>
    public async Task<Result<BillingSlipResult>> SendAsync(Guid receivableId, Guid slipId, BillingSlipSendInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId && s.ReceivableId == receivableId, cancellationToken);
        if (slip is null)
        {
            return NotFound();
        }

        if (slip.Status != BillingSlipStatus.Issued || slip.PdfAttachmentId is null)
        {
            return Error.Conflict("billing_slip_not_issued", "Só o boleto emitido pelo banco pode ser enviado.");
        }

        var receivable = await db.Receivables.AsNoTracking().FirstAsync(r => r.Id == receivableId, cancellationToken);
        var to = input.To?.Trim();
        if (string.IsNullOrEmpty(to))
        {
            to = (await parties.BillingContactAsync(receivable.PartyId, cancellationToken))?.Email?.Trim();
        }

        if (string.IsNullOrEmpty(to) || !EmailAddresses.IsPlain(to))
        {
            return ReceivableService.Invalid("to", "Informe um e-mail válido para o cliente (só o endereço, sem nome).");
        }

        var cc = (input.Cc ?? []).Select(c => c?.Trim() ?? string.Empty).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cc.Count > 3 || cc.Any(c => !EmailAddresses.IsPlain(c)))
        {
            return ReceivableService.Invalid("cc", "Informe até 3 cópias, cada uma com um e-mail válido.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sent = await mailer.SendOrScheduleAsync(slip, receivable, to, cc, currentUser.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BillingSlipResult(await ViewAsync(slip, cancellationToken), sent ? "Boleto enviado" : BillingSlipMailer.LimitMessage);
    }
}
