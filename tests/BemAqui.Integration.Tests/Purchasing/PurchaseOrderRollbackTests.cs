using System.Net;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CA 12 (ADR 0024 §1): a aprovação e os títulos previstos estão na mesma transação. Com o <see cref="IPayables"/> falhando na criação
/// por parcela, a aprovação é recusada e nada grava: o pedido continua <c>PendingApproval</c>, sem título e sem decisão. Banco desta classe.
/// </summary>
public class PurchaseOrderRollbackTests(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy), IClassFixture<TenancyFixture>
{
    protected override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<PayablesContract>();
        services.AddScoped<IPayables>(sp => new FailingPayables(sp.GetRequiredService<PayablesContract>()));
    }

    [Fact]
    public async Task Falha_do_IPayables_desfaz_a_aprovacao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync("purchasing:view,create,edit");
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 2m, 75m, null))));
        var id = order.GetProperty("id").GetGuid();

        var refused = await DecideAsync(owner, order, true);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("payables_unavailable");
        (await GetAsync(owner, id)).GetProperty("status").GetString().Should().Be("PendingApproval");
        (await PayablesOfAsync(id)).Should().BeEmpty();
        (await WithDbAsync(db => db.PurchaseOrderApprovals.CountAsync(a => a.OrderId == id && a.Decision == BemAqui.Purchasing.Domain.PurchaseDecision.Approved))).Should().Be(0);
        (await WithDbAsync(db => db.PurchaseOrderLevels.SingleAsync(l => l.OrderId == id))).Status.Should().Be(BemAqui.Purchasing.Domain.PurchaseLevelStatus.Pending);
    }

    /// <summary>O contrato de produção, com a criação por parcela falhando (todos os membros delegam).</summary>
    private sealed class FailingPayables(IPayables inner) : IPayables
    {
        public Task<Result<PayableRef>> CreateFromOriginAsync(
            DocumentOrigin origin, Guid originId, Guid partyId, Guid? projectId, decimal amount, DateOnly? dueDate, bool estimatedDueDate, string description, bool planned,
            CancellationToken cancellationToken = default) =>
            inner.CreateFromOriginAsync(origin, originId, partyId, projectId, amount, dueDate, estimatedDueDate, description, planned, cancellationToken);

        public Task<Result<PayableRef>> ConfirmAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly dueDate, CancellationToken cancellationToken = default) =>
            inner.ConfirmAsync(origin, originId, amount, dueDate, cancellationToken);

        public Task<Result<PayableRef>> UpdatePlannedAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly? dueDate, CancellationToken cancellationToken = default) =>
            inner.UpdatePlannedAsync(origin, originId, amount, dueDate, cancellationToken);

        public Task<Result> CancelFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default) =>
            inner.CancelFromOriginAsync(origin, originId, reason, cancellationToken);

        public Task<PayableRef?> FindByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) => inner.FindByOriginAsync(origin, originId, cancellationToken);

        public Task<Result<IReadOnlyList<PayableRef>>> CreatePlannedInstallmentsAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<IReadOnlyList<PayableRef>>>(Error.Conflict("payables_unavailable", "Contas a pagar indisponível no teste."));

        public Task<Result<IReadOnlyList<PayableRef>>> ReplacePlannedAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<IReadOnlyList<PayableRef>>>(Error.Conflict("payables_unavailable", "Contas a pagar indisponível no teste."));

        public Task<Result> CancelAllFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default) =>
            inner.CancelAllFromOriginAsync(origin, originId, reason, cancellationToken);

        public Task<Result<IReadOnlyList<PayableRef>>> CreateOpenInstallmentsAsync(DocumentOrigin origin, Guid originId, OpenPayables plan, CancellationToken cancellationToken = default) =>
            inner.CreateOpenInstallmentsAsync(origin, originId, plan, cancellationToken);

        public Task<IReadOnlyList<PayableRef>> ListByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) =>
            inner.ListByOriginAsync(origin, originId, cancellationToken);

        public Task<IReadOnlyList<ProjectPayable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ByProjectAsync(projectId, cancellationToken);

        public Task<IReadOnlyList<PayableOriginStatus>> StatusByOriginAsync(DocumentOrigin origin, IReadOnlyCollection<Guid> originIds, CancellationToken cancellationToken = default) => inner.StatusByOriginAsync(origin, originIds, cancellationToken);
    }
}
