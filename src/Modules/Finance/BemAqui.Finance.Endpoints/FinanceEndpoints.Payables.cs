using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Contas a pagar e recorrentes (FIN-02): sem rota de baixa (só a OP paga, CA 10) nem de exclusão de título.</summary>
internal static partial class FinanceEndpoints
{
    private static void MapPayables(RouteGroupBuilder payables)
    {
        payables.MapGet(string.Empty, async (string? q, string? situation, Guid? partyId, DateOnly? dueFrom, DateOnly? dueTo, int? page, int? pageSize, string? sort, PayableService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PayableListQuery(q, situation, partyId, dueFrom, dueTo), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListPayables").Produces<PayablePage>();
        payables.MapGet("/{id:guid}", async (Guid id, PayableService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetPayable").Produces<PayableDetail>();
        payables.MapGet("/{id:guid}/payment-method", async (Guid id, PayableService service, CancellationToken ct) => (await service.PaymentMethodAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetPayablePaymentMethod").Produces<PaymentDetailsView>();
        payables.MapPost(string.Empty, async (PayableInput input, PayableService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/payables/" + saved.Items[0].Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreatePayable").Produces<PayablesCreated>(StatusCodes.Status201Created);
        payables.MapPost("/installments", async (PayableInstallmentsInput input, PayableService service, CancellationToken ct) =>
                (await service.CreateInstallmentsAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/payables/" + saved.Items[0].Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreatePayableInstallments").Produces<PayablesCreated>(StatusCodes.Status201Created);
        payables.MapPut("/{id:guid}", async (Guid id, PayableUpdateInput input, PayableService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdatePayable").Produces<PayableDetail>();
        payables.MapPut("/{id:guid}/payment-method", async (Guid id, PaymentMethodInput input, PayableService service, CancellationToken ct) => (await service.SetPaymentMethodAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("SetPayablePaymentMethod").Produces<PaymentDetailsView>();
        payables.MapPost("/{id:guid}/cancel", async (Guid id, ReasonInput input, PayableService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("CancelPayable").Produces<PayableDetail>();
    }

    private static void MapSuppliers(RouteGroupBuilder suppliers) =>
        suppliers.MapGet(string.Empty, async (string? q, PayableService service, CancellationToken ct) => Results.Ok(await service.SuppliersAsync(q, ct)))
            .RequirePermission("finance.view").WithName("ListFinanceSuppliers").Produces<IReadOnlyList<FinanceSupplierOption>>();

    private static void MapProjects(RouteGroupBuilder projects) =>
        projects.MapGet(string.Empty, async (string? q, PayableService service, CancellationToken ct) => Results.Ok(await service.ProjectsAsync(q, ct)))
            .RequirePermission("finance.view").WithName("ListFinanceProjects").Produces<IReadOnlyList<FinanceProjectOption>>();

    private static void MapRecurringExpenses(RouteGroupBuilder recurring)
    {
        recurring.MapGet(string.Empty, async (RecurringExpenseService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("finance.view").WithName("ListRecurringExpenses").Produces<IReadOnlyList<RecurringExpenseDetail>>();
        recurring.MapPost(string.Empty, async (RecurringExpenseInput input, RecurringExpenseService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/recurring-expenses/" + saved.Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreateRecurringExpense").Produces<RecurringExpenseDetail>(StatusCodes.Status201Created);
        recurring.MapPut("/{id:guid}", async (Guid id, RecurringExpenseInput input, RecurringExpenseService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateRecurringExpense").Produces<RecurringExpenseDetail>();
        recurring.MapDelete("/{id:guid}", async (Guid id, RecurringExpenseService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeleteRecurringExpense");
    }
}
