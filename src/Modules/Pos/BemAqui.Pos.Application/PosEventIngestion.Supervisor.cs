using System.Globalization;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// PDV-04: as aprovações de supervisor citadas pelos eventos (CA 10 e 11) e o cancelamento da venda concluída da sessão atual (CA 6 a 9). A aprovação
/// foi conferida no servidor quando o supervisor digitou o PIN (permissão, PIN, supervisor diferente do operador); aqui se confere que o evento usa a
/// aprovação daquela ação, daquela venda, daquele caixa e daquelas pessoas, uma vez só.
/// </summary>
public sealed partial class PosEventIngestion
{
    public const string OtherRegisterMessage = "Esta venda é de outro caixa. Use a devolução.";

    /// <summary>Notificação do Proprietário no cancelamento de venda paga (CA 9).</summary>
    public const string CancelNotificationType = "pos.sale_cancelled";

    private async Task CheckApprovalsAsync(SyncContext context, Guid eventId, Guid saleId, IReadOnlyList<ReplayApproval> cited, List<string> problems, List<SupervisorApproval> found,
        CancellationToken ct)
    {
        if (cited.GroupBy(a => a.ApprovalId).Any(g => g.Count() > 1))
        {
            problems.Add("A mesma aprovação de supervisor foi usada em mais de uma ação.");
            return;
        }

        foreach (var approval in cited)
        {
            var row = await db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == approval.ApprovalId, ct);
            if (row is null || row.StoreId != context.Store.Id || row.RegisterId != context.Register.Id || row.DeviceId != context.Device.DeviceId
                || row.Action != approval.Action || row.SaleId != saleId || row.OperatorId != approval.OperatorId || row.SupervisorId != approval.SupervisorId
                || (row.LineId is not null && row.LineId != approval.LineId) || (row.UsedByEventId is not null && row.UsedByEventId != eventId))
            {
                problems.Add($"{SupervisorActions.Label(approval.Action)} com uma aprovação de supervisor que não confere (outra venda, ação, pessoa ou caixa, ou já usada).");
                continue;
            }

            found.Add(row);
        }
    }

    /// <summary>
    /// Cancelamento da venda concluída (CA 6 a 9): o operador com <c>pos.cancel_sale</c> no instante e a aprovação de outro supervisor; só a venda da
    /// sessão aberta deste caixa (CA 8: senão, devolução, PDV-06); pagamento que não é dinheiro só com o estorno confirmado no caixa e, no cartão, o NSU do
    /// estorno (CA 7). O <c>Sales</c> cancela (auditado) e publica o <c>SaleCancelled</c> (estoque e financeiro reagem); o dinheiro da venda sai da gaveta
    /// da sessão; o Proprietário é avisado (CA 9). O mesmo cancelamento de novo não muda nada.
    /// </summary>
    private async Task<Projection> ProjectCancelAsync(SyncContext context, Guid eventId, JsonElement root, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var saleId = PosSaleReplay.GuidOf(root, "saleId");
        var operatorId = PosSaleReplay.GuidOf(root, "operatorId");
        var supervisorId = PosSaleReplay.GuidOf(root, "authorizedBy");
        var approvalId = PosSaleReplay.GuidOf(root, "approvalId");
        var reason = PosSaleReplay.StringOf(root, "reason")?.Trim();
        var refund = PosSaleReplay.StringOf(root, "refundNsu")?.Trim();
        var refundConfirmed = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("refundConfirmed", out var r) && r.ValueKind == JsonValueKind.True;
        var at = PosSaleReplay.DateOf(root, "at") ?? occurredAt;
        var projection = new Projection { SaleId = saleId, SaleNumber = PosSaleReplay.StringOf(root, "number"), OperatorId = operatorId };
        var problems = new List<string>();
        if (saleId is null || approvalId is null || supervisorId is null || operatorId is null)
        {
            problems.Add("Cancelamento sem a venda, o operador, o supervisor ou a aprovação.");
        }
        else if (supervisorId == operatorId)
        {
            problems.Add("Outra pessoa precisa aprovar.");
        }

        if (string.IsNullOrWhiteSpace(reason) || PosSaleReplay.TextProblem(reason, PosSaleReplay.MaxReasonLength, "motivo do cancelamento") is not null)
        {
            problems.Add("Cancelamento sem motivo, longo demais ou com sequência que parece número de cartão.");
        }

        if (PosSaleReplay.TextProblem(refund, PosSaleReplay.MaxNsuLength, "NSU do estorno") is { } refundProblem)
        {
            problems.Add(refundProblem);
        }

        var approvals = new List<SupervisorApproval>();
        if (problems.Count == 0 && !context.Trusted)
        {
            var checks = new[]
            {
                new ReplayCheck(operatorId, at, "quem cancelou a venda", "pos.cancel_sale", NeedsPinSession: true),
            };
            await CheckPeopleAsync(context, checks, problems, [], ct);
            await CheckApprovalsAsync(context, eventId, saleId!.Value, [new ReplayApproval(approvalId!.Value, SupervisorAction.CancelSale, operatorId, supervisorId, null, at)],
                problems, approvals, ct);
        }

        if (problems.Count > 0 && !context.Trusted)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Validation, "Cancelamento recusado na conferência: " + string.Join(" ", problems.Distinct()), new { problems });
            return projection;
        }

        var sale = saleId is { } id ? await saleLookup.FindAsync(id, ct) : null;
        if (sale is null)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Validation, "Cancelamento de uma venda que não está no servidor (a venda pode estar na quarentena).");
            return projection;
        }

        projection.SaleNumber = sale.Number;
        if (sale.Status == SaleStatus.Cancelled)
        {
            return projection; // reenvio ou cancelado na retaguarda: nada a fazer
        }

        var session = sale.CashSessionId is { } sessionId ? await db.CashSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct) : null;
        if (sale.Status != SaleStatus.Completed || sale.RegisterId != context.Register.Id || session is null || session.Status != CashSessionStatus.Open
            || session.CountStartedAt is not null || PosSaleReplay.GuidOf(root, "cashSessionId") != session.Id)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.CashSession, OtherRegisterMessage);
            return projection;
        }

        // CA 7: dinheiro sai da gaveta; o resto só com o estorno confirmado no caixa (cartão, com o NSU do estorno).
        var methods = await context.MethodsAsync(db, ct);
        var captured = sale.Payments.Where(p => p.Status == SalePaymentStatus.Captured).ToList();
        var types = captured.Select(p => methods.TryGetValue(p.PaymentMethodId, out var m) ? m.Type : (PaymentMethodType?)null).ToList();
        var needsRefund = types.Any(t => t != PaymentMethodType.Cash);
        var card = types.Any(t => t is { } type && PosCardTerms.IsCard(type));
        if (!context.Trusted && ((needsRefund && !refundConfirmed) || (card && string.IsNullOrWhiteSpace(refund))))
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Payment, "Cancelamento sem a confirmação do estorno na maquininha (e, no cartão, o NSU do estorno).");
            return projection;
        }

        var cancelled = await sales.CancelAsync(new SaleCancellationRequest(sale.Id, reason!, CapturedRefundedOutside: true, CancelledBy: operatorId), ct);
        if (cancelled.IsFailure)
        {
            throw new ProjectionFailedException(SyncDivergenceKind.Validation, "A venda não foi cancelada no núcleo de vendas: " + cancelled.Error!.Message);
        }

        var now = clock.GetUtcNow();
        if (await db.SessionSales.FirstOrDefaultAsync(s => s.SaleId == sale.Id, ct) is { } kept)
        {
            kept.CashAmount = 0;
        }

        foreach (var approval in approvals)
        {
            approval.Use(eventId, now, refund);
        }

        // CA 9: o Proprietário sabe de todo cancelamento de venda paga (venda, valor, operador, supervisor e motivo).
        if (captured.Count > 0)
        {
            var operatorName = operatorId is { } o ? await users.DisplayNameAsync(o, ct) : null;
            var supervisorName = supervisorId is { } s ? await users.DisplayNameAsync(s, ct) : null;
            var title = string.Create(CultureInfo.GetCultureInfo("pt-BR"),
                $"Venda {sale.Number} de {sale.Total:C} cancelada no caixa {context.Register.Number:00} ({context.Store.Code}): operador {operatorName ?? "—"}, supervisor {supervisorName ?? "—"}. Motivo: {reason}");
            foreach (var owner in await users.OwnerIdsAsync(ct))
            {
                notifications.Notify(owner, CancelNotificationType, title, SupervisorApprovalService.Link);
            }
        }

        return projection;
    }
}
