using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application.Reconciliation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Conciliação bancária (BNK-03b CA 13): ver com <c>finance.view</c>; conciliar, desfazer, baixar recebimento e editar regras com
/// <c>finance.edit</c>; lançar sem OP com <c>finance.pay</c> e, a partir de saída, o 2FA de novo (D6); fechar com <c>finance.approve</c>.
/// O período fechado, o teto do OFX e a segregação de quem importou ficam no serviço. Linha ou conciliação de outra conta: 404;
/// transação de outra conta: 422.
/// </summary>
internal static partial class FinanceEndpoints
{
    private static void MapReconciliation(RouteGroupBuilder finance)
    {
        var account = finance.MapGroup("/bank-accounts/{id:guid}");
        account.MapGet("/reconciliation", async (Guid id, DateOnly? from, DateOnly? to, ReconciliationService service, CancellationToken ct) =>
                (await service.ViewAsync(id, from, to, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetReconciliation").Produces<ReconciliationView>();
        account.MapGet("/reconciliation/candidates", async (Guid id, DateOnly date, bool outflow, bool? paymentsOnly, ReconciliationService service, CancellationToken ct) =>
                (await service.CandidatesAsync(id, date, outflow, paymentsOnly == true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ListReconciliationCandidates").Produces<IReadOnlyList<ReconTransaction>>();
        account.MapGet("/reconciliation/pending-lines", async (Guid id, DateOnly date, bool outflow, ReconciliationService service, CancellationToken ct) =>
                (await service.PendingLinesAsync(id, date, outflow, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ListReconciliationPendingLines").Produces<IReadOnlyList<ReconLine>>();
        account.MapPost("/reconciliations", async (Guid id, ReconcileInput input, ReconciliationService service, CancellationToken ct) => (await service.ReconcileAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("Reconcile").Produces<ReconcileResult>();
        account.MapPost("/reconciliations/accept-high", async (Guid id, AcceptHighInput input, ReconciliationService service, CancellationToken ct) =>
                (await service.AcceptHighAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("AcceptHighReconciliations").Produces<AcceptHighResult>();
        account.MapPost("/reconciliations/{rid:guid}/undo", async (Guid id, Guid rid, UndoReconciliationInput input, ReconciliationService service, CancellationToken ct) =>
                (await service.UndoAsync(id, rid, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UndoReconciliation").Produces<ReconcileResult>();
        account.MapPost("/reconciliation/lines/{lineId:guid}/bank-entry", async (Guid id, Guid lineId, LineBankEntryInput input, ReconciliationService service, HttpContext http, TimeProvider clock,
                CancellationToken ct) =>
            {
                // D6: lançar sem OP a partir de uma saída pede a prova recente do 2FA; a entrada não.
                if (await service.IsDebitAsync(id, lineId, ct) == true && !RecentTwoFactorEndpoints.HasRecentProof(http.User, clock.GetUtcNow()))
                {
                    return RecentTwoFactorEndpoints.Refused(http.User);
                }

                return (await service.BankEntryAsync(id, lineId, input, ct)).ToHttpResult();
            })
            .RequirePermission("finance.pay").WithName("CreateBankEntryFromLine").Produces<ReconcileResult>();
        account.MapPost("/reconciliation/lines/{lineId:guid}/settle", async (Guid id, Guid lineId, LineSettlementInput input, ReconciliationService service, CancellationToken ct) =>
                (await service.SettleAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("SettleFromLine").Produces<ReconcileResult>();
        account.MapGet("/reconciliation/lines/{lineId:guid}/history", async (Guid id, Guid lineId, ReconciliationService service, CancellationToken ct) =>
                (await service.LineHistoryAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetStatementLineHistory").Produces<ReconHistory>();
        account.MapGet("/reconciliation/transactions/{transactionId:guid}/history", async (Guid id, Guid transactionId, ReconciliationService service, CancellationToken ct) =>
                (await service.TransactionHistoryAsync(id, transactionId, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetTransactionReconciliationHistory").Produces<ReconHistory>();
        account.MapGet("/reconciliation/closing-preview", async (Guid id, DateOnly until, ReconciliationService service, CancellationToken ct) =>
                (await service.ClosingPreviewAsync(id, until, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetReconciliationClosingPreview").Produces<ClosingPreview>();
        account.MapPost("/reconciliation/close", async (Guid id, CloseReconciliationInput input, ReconciliationService service, CancellationToken ct) => (await service.CloseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.approve").WithName("CloseReconciliation").Produces<ReconciliationClosingView>();

        finance.MapGet("/bank-statement-rules", async (ReconciliationService service, CancellationToken ct) => Results.Ok(await service.RulesAsync(ct)))
            .RequirePermission("finance.view").WithName("ListBankStatementRules").Produces<IReadOnlyList<BankStatementRuleView>>();
        finance.MapPost("/bank-statement-rules", async (BankStatementRuleInput input, ReconciliationService service, CancellationToken ct) => (await service.CreateRuleAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("CreateBankStatementRule").Produces<BankStatementRuleView>();
        finance.MapPut("/bank-statement-rules/{ruleId:guid}", async (Guid ruleId, BankStatementRuleInput input, ReconciliationService service, CancellationToken ct) =>
                (await service.UpdateRuleAsync(ruleId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateBankStatementRule").Produces<BankStatementRuleView>();
    }
}
