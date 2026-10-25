using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class PurchaseOrderService
{
    /// <summary>
    /// "Enviar para aprovação" (CA 5 a 8): o pedido em rascunho válido passa a <c>PendingApproval</c> com os níveis resolvidos pelo
    /// <see cref="IApprovalRouting"/>; sem aprovador elegível, 422 <c>approval_not_viable</c> e o pedido continua <c>Draft</c>.
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> SubmitAsync(Guid id, PurchaseOrderVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return Error.Conflict(NotDraftCode, $"O pedido {order.Number} já foi enviado para aprovação.");
        }

        if (order.Version != input.Version)
        {
            return Stale();
        }

        // O rascunho pode ter ficado inválido (fornecedor sem papel, item inativo, projeto encerrado): confere de novo antes de enviar.
        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var check = await PrepareAsync(
            new Draft(order.PartyId, order.ProjectId, order.PaymentTermId, order.ExpectedDate, order.Freight, order.IpiPct, order.Notes,
                lines.Select(l => new PurchaseOrderLineInput(l.ItemId, l.Quantity, null, l.UnitPrice, l.RequestLineIds)).ToList()),
            order, lines.SelectMany(l => l.RequestLineIds).ToHashSet(), cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (order.ExpectedDate < Today)
        {
            return Invalid("expectedDate", "A data prevista de entrega já passou. Atualize antes de enviar.");
        }

        var now = clock.GetUtcNow();
        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var opened = await OpenAttemptAsync(order, now, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error!;
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Aprovar pedido" (CA 9, 12): decide o nível pendente mais baixo; no último, o pedido fica <c>Approved</c> e os previstos nascem juntos.</summary>
    public Task<Result<PurchaseOrderDetail>> ApproveAsync(Guid id, PurchaseOrderDecisionInput input, CancellationToken cancellationToken = default) =>
        DecideAsync(id, input, true, cancellationToken);

    /// <summary>"Reprovar pedido" (CA 14): comentário obrigatório; volta a <c>Draft</c> e o solicitante é avisado.</summary>
    public Task<Result<PurchaseOrderDetail>> RejectAsync(Guid id, PurchaseOrderDecisionInput input, CancellationToken cancellationToken = default) =>
        DecideAsync(id, input, false, cancellationToken);

    private async Task<Result<PurchaseOrderDetail>> DecideAsync(Guid id, PurchaseOrderDecisionInput input, bool approve, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("purchasing", "approve", ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem aprova o pedido é quem tem a permissão Aprovar em Compras.");
        }

        var comment = Clean(input.Comment);
        if (!approve && comment is null)
        {
            return Invalid("comment", "Escreva o motivo da reprovação.");
        }

        if (comment is { Length: > PurchaseOrderRules.MaxComment })
        {
            return Invalid("comment", $"Escreva o comentário com até {PurchaseOrderRules.MaxComment} caracteres.");
        }

        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.PendingApproval || order.Version != input.Version)
        {
            return Error.Conflict(DecidedCode, "O pedido já foi decidido ou mudou desde que você abriu. Recarregue.");
        }

        var level = await CurrentLevelAsync(order, ct);
        if (level is null)
        {
            return Error.Conflict(DecidedCode, "O pedido não tem nível pendente. Recarregue.");
        }

        if (await DecisionErrorAsync(order, level, ct) is { } denied)
        {
            return denied;
        }

        var now = clock.GetUtcNow();
        var self = currentUser.UserId is { } me && me == order.RequestedBy;
        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        db.PurchaseOrderApprovals.Add(new PurchaseOrderApproval
        {
            OrderId = order.Id, Attempt = order.Attempt, Level = level.Level, Decision = approve ? PurchaseDecision.Approved : PurchaseDecision.Rejected, ApprovalRuleId = level.ApprovalRuleId,
            ReferenceAmount = level.ReferenceAmount, Comment = comment, DecidedBy = currentUser.UserId, DecidedAt = now, SelfApproved = approve && self,
        });
        level.Status = approve ? PurchaseLevelStatus.Approved : PurchaseLevelStatus.Rejected;
        order.UpdatedAt = now;
        if (!approve)
        {
            foreach (var other in await db.PurchaseOrderLevels.Where(l => l.OrderId == order.Id && l.Attempt == order.Attempt && l.Status == PurchaseLevelStatus.Pending && l.Id != level.Id).ToListAsync(ct))
            {
                other.Status = PurchaseLevelStatus.Closed;
            }

            // Reaprovação de pedido com recebimento (CMP-04 CA 14): os preços corrigidos voltam e o pedido segue parcialmente recebido.
            if (order.ApprovedTotal is not null && await db.PurchaseOrderLines.AnyAsync(l => l.OrderId == order.Id && l.QuantityReceived > 0m, ct))
            {
                await RevertCorrectionsAsync(order, ct);
            }
            else
            {
                order.Status = PurchaseOrderStatus.Draft;
            }

            order.RejectionComment = comment;
            if (order.RequestedBy is { } requester)
            {
                notifications.Notify(requester, NotificationType, $"Pedido {order.Number} reprovado: {Cut(comment!, 120)}", Link(order.Id));
            }

            events.Publish(new PurchaseOrderRejected(order.Id, order.Attempt));
            return await SavedDetailAsync(order, ct);
        }

        order.SelfApproved = order.SelfApproved || self;
        if (self)
        {
            await NotifySelfApprovedAsync(order, ct);
        }

        var next = await db.PurchaseOrderLevels.Where(l => l.OrderId == order.Id && l.Attempt == order.Attempt && l.Status == PurchaseLevelStatus.Pending && l.Level > level.Level)
            .OrderBy(l => l.Level).FirstOrDefaultAsync(ct);
        if (next is not null)
        {
            await NotifyLevelAsync(order, next, ct);
            return await SavedDetailAsync(order, ct);
        }

        // Último nível (CA 12, ADR 0024 §1): pedido aprovado e títulos previstos por parcela na mesma transação; falha do IPayables desfaz tudo.
        // Com recebimento (reaprovação da correção de preço, CMP-04 CA 14), o pedido volta a parcialmente recebido e o previsto é o saldo.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var received = await db.PurchaseOrderLines.AnyAsync(l => l.OrderId == order.Id && l.QuantityReceived > 0m, ct);
        order.Status = received ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.Approved;
        order.ApprovedAt = now;
        order.ApprovedTotal = order.Total;
        order.RejectionComment = null;
        var planned = await PlanPayablesAsync(order, ct);
        if (planned.IsFailure)
        {
            return planned.Error!;
        }

        events.Publish(new PurchaseOrderApproved(order.Id, order.PartyId, order.ProjectId, order.Total, order.PaymentTermId, order.ExpectedDate));
        if (order.RequestedBy is { } who && who != currentUser.UserId)
        {
            notifications.Notify(who, NotificationType, $"Pedido {order.Number} aprovado. Previsão lançada no caixa.", Link(order.Id));
        }

        if (await SaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(ct);
        return await DetailAsync(order.Id, ct);
    }

    /// <summary>
    /// Quem decide (CA 9, 10): está na regra do nível (<see cref="IApprovalRouting.EligibleAsync"/>), com <c>purchasing.approve</c>, não
    /// decidiu outro nível desta tentativa e não é quem pediu (exceto o Proprietário único, pelo <c>SoleOwnerRule</c>).
    /// </summary>
    private async Task<Error?> DecisionErrorAsync(PurchaseOrder order, PurchaseOrderLevel level, CancellationToken ct)
    {
        var me = currentUser.UserId;
        var other = await db.PurchaseOrderApprovals.AsNoTracking()
            .Where(a => a.OrderId == order.Id && a.Attempt == order.Attempt && a.DecidedBy == me && a.Decision == PurchaseDecision.Approved && a.Level != level.Level)
            .Select(a => (int?)a.Level).FirstOrDefaultAsync(ct);
        if (other is { } decided)
        {
            return Error.Forbidden("already_decided_level", $"Você já aprovou o nível {decided} deste pedido.");
        }

        if (me is { } user && (await EligibleNowAsync(order, level, ct)).Contains(user))
        {
            return null;
        }

        return me is { } requester && requester == order.RequestedBy
            ? Error.Forbidden("own_purchase_order", "Quem pediu não aprova o próprio pedido.")
            : Error.Forbidden("outside_approval_rule", "Este pedido não está na sua alçada.");
    }

    /// <summary>Elegíveis do nível agora: os da regra gravada, menos quem já aprovou outro nível desta tentativa.</summary>
    private async Task<IReadOnlyList<Guid>> EligibleNowAsync(PurchaseOrder order, PurchaseOrderLevel level, CancellationToken ct)
    {
        var eligible = await routing.EligibleAsync(ApprovalDocumentTypes.PurchaseOrder, level.Level, level.ApprovalRuleId, order.RequestedBy, ct);
        var decided = await db.PurchaseOrderApprovals.AsNoTracking()
            .Where(a => a.OrderId == order.Id && a.Attempt == order.Attempt && a.Decision == PurchaseDecision.Approved && a.Level != level.Level)
            .Select(a => a.DecidedBy).ToListAsync(ct);
        return eligible.Where(e => !decided.Contains(e)).ToList();
    }

    private Task<PurchaseOrderLevel?> CurrentLevelAsync(PurchaseOrder order, CancellationToken ct) =>
        db.PurchaseOrderLevels.Where(l => l.OrderId == order.Id && l.Attempt == order.Attempt && l.Status == PurchaseLevelStatus.Pending).OrderBy(l => l.Level).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Nova tentativa (CA 5 a 8): valor de referência pelos pedidos do fornecedor em 30 dias (CA 6), a conta dos títulos (CA 19), os níveis
    /// pelo <see cref="IApprovalRouting"/>, o envio na linha do tempo e o aviso ao nível 1.
    /// </summary>
    private async Task<Result> OpenAttemptAsync(PurchaseOrder order, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-PurchaseOrderRules.ReferenceWindowDays);
        var counted = PurchaseOrderRules.CountsForReference.ToList();
        var recent = await db.PurchaseOrders.AsNoTracking()
            .Where(o => o.PartyId == order.PartyId && o.Id != order.Id && o.SubmittedAt >= since && counted.Contains(o.Status)).Select(o => o.Total).ToListAsync(ct);
        var reference = PurchaseOrderRules.ReferenceAmount(order.Total, recent.Sum());
        var account = await AccountIdAsync(ct);
        if (account.IsFailure)
        {
            return account.Error!;
        }

        var resolved = await routing.ResolveAsync(ApprovalDocumentTypes.PurchaseOrder, reference, order.Total, account.Value, order.RequestedBy, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var warning = resolved.Value.SplitDetected && recent.Count > 0 ? PurchaseOrderRules.SplitWarning(recent.Count + 1, reference) : null;
        order.Attempt++;
        order.Status = PurchaseOrderStatus.PendingApproval;
        order.SubmittedAt = now;
        order.ReferenceAmount = reference;
        order.AccountId = account.Value;
        order.RejectionComment = null;
        order.UpdatedAt = now;
        PurchaseOrderLevel? first = null;
        foreach (var level in resolved.Value.Levels)
        {
            var row = new PurchaseOrderLevel
            {
                OrderId = order.Id, Attempt = order.Attempt, Level = level.Level, ApprovalRuleId = level.RuleId, ApproverName = Cut(level.ApproverName, 120), ReferenceAmount = reference,
                SplitWarning = warning,
            };
            db.PurchaseOrderLevels.Add(row);
            first ??= row;
        }

        db.PurchaseOrderApprovals.Add(new PurchaseOrderApproval
        {
            OrderId = order.Id, Attempt = order.Attempt, Level = 0, Decision = PurchaseDecision.Submitted, ReferenceAmount = reference, Comment = warning, DecidedBy = currentUser.UserId,
            DecidedAt = now,
        });
        events.Publish(new PurchaseOrderSubmitted(order.Id, order.Attempt, order.Total, order.RequestedBy));
        await NotifyLevelAsync(order, first!, ct, resolved.Value.Levels[0].Eligible);
        return Result.Success();
    }

    /// <summary>Encerra a tentativa aberta (edição ou cancelamento): os níveis pendentes ficam <c>Closed</c> e o evento entra na linha do tempo.</summary>
    private async Task CloseAttemptAsync(PurchaseOrder order, PurchaseDecision decision, string comment, DateTimeOffset now, CancellationToken ct)
    {
        if (order.Attempt > 0)
        {
            foreach (var level in await db.PurchaseOrderLevels.Where(l => l.OrderId == order.Id && l.Attempt == order.Attempt && l.Status == PurchaseLevelStatus.Pending).ToListAsync(ct))
            {
                level.Status = PurchaseLevelStatus.Closed;
            }
        }

        db.PurchaseOrderApprovals.Add(new PurchaseOrderApproval
        {
            OrderId = order.Id, Attempt = order.Attempt, Level = 0, Decision = decision, Comment = Cut(comment, PurchaseOrderRules.MaxComment), DecidedBy = currentUser.UserId, DecidedAt = now,
        });
    }

    /// <summary>
    /// Títulos previstos da aprovação (CA 12, 15): um por parcela da condição sobre o total (ou, com recebimento, sobre o saldo a receber,
    /// CMP-04), a partir da data prevista de entrega, com a conta e a natureza dos parâmetros; na reaprovação, troca os previstos de antes
    /// (<see cref="IPayables.ReplacePlannedAsync"/>).
    /// </summary>
    private Task<Result> PlanPayablesAsync(PurchaseOrder order, CancellationToken ct) => ReplanAsync(order, $"pedido {order.Number} reaprovado", ct);
    /// <summary>Conta dos títulos do pedido (CA 19): o código de <c>purchasing.parameters</c> ("2.1"), conta analítica ativa; senão "3.9".</summary>
    internal async Task<Result<Guid>> AccountIdAsync(CancellationToken ct)
    {
        var defaults = await parameters.PayableDefaultsAsync(ct);
        var postable = await chart.PostableAsync(null, ct);
        var account = postable.FirstOrDefault(a => a.Code == defaults.AccountCode) ?? postable.FirstOrDefault(a => a.Code == "3.9");
        return account is null
            ? Error.Conflict("payable_account_missing", "Cadastre a conta 2.1 Materiais (ou outra analítica ativa) para os títulos dos pedidos.")
            : account.Id;
    }

    /// <summary>Sino e e-mail aos aprovadores do nível (CA 8): o e-mail leva só o número, o fornecedor, o total e o link autenticado.</summary>
    private async Task NotifyLevelAsync(PurchaseOrder order, PurchaseOrderLevel level, CancellationToken ct, IReadOnlyList<Guid>? eligible = null)
    {
        events.Publish(new PurchaseOrderLevelReleased(order.Id, order.Attempt, level.Level));
        var who = eligible ?? await EligibleNowAsync(order, level, ct);
        var party = await parties.FindAsync(order.PartyId, ct);
        var supplier = party is null ? "Fornecedor" : party.TradeName ?? party.LegalName;
        var total = PurchaseOrderRules.Money(order.Total);
        foreach (var user in who)
        {
            notifications.Notify(user, NotificationType, $"Pedido de compra {order.Number} de {total} aguarda sua aprovação" + (level.Level > 1 ? $" (nível {level.Level})" : string.Empty),
                Link(order.Id) + "?acao=aprovar");
            if (await users.EmailOfAsync(user, ct) is { } to)
            {
                email.Enqueue(EmailTemplate, to, new Dictionary<string, object?>
                {
                    ["number"] = order.Number, ["supplier"] = supplier, ["total"] = total, ["link"] = links.Absolute(Link(order.Id) + "?acao=aprovar"),
                }, AttachmentOwner, order.Id);
            }
        }
    }

    /// <summary>"Autoaprovado" (<c>SoleOwnerRule</c>): o Proprietário é avisado na mesma transação.</summary>
    private async Task NotifySelfApprovedAsync(PurchaseOrder order, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, NotificationType, $"Pedido {order.Number} aprovado por quem pediu ({SoleOwnerRule.SelfApprovedLabel}).", Link(order.Id));
        }
    }

    private async Task<Result<PurchaseOrderDetail>> SavedDetailAsync(PurchaseOrder order, CancellationToken ct) =>
        await SaveAsync(ct) is { } conflict ? conflict : await DetailAsync(order.Id, ct);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
