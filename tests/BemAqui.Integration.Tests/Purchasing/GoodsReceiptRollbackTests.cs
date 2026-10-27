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
/// CMP-04 CA 5 (ADR 0023 §1, ADR 0024 §1): o recebimento, a entrada no estoque e os títulos estão na mesma transação. Com o
/// <see cref="IPayables"/> recusando os títulos do recebimento, nada grava: nem recebimento, nem movimento, nem recebido no pedido, nem lote.
/// </summary>
public class GoodsReceiptRollbackTests(TenancyFixture tenancy) : GoodsReceiptTestBase(tenancy), IClassFixture<TenancyFixture>
{
    protected override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<PayablesContract>();
        services.AddScoped<IPayables>(sp => new RefusingPayables(sp.GetRequiredService<PayablesContract>()));
    }

    [Fact]
    public async Task Recusa_do_IPayables_desfaz_o_recebimento_e_a_entrada()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await LotItemAsync(tracksLot: true);
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 2m, 50m, null));
        var orderId = order.GetProperty("id").GetGuid();

        var refused = await ReceiveAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 2m, "LT-RB")));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("payables_unavailable");
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == orderId))).Should().Be(0);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == item))).Should().Be(0);
        (await WithDbAsync(db => db.Lots.CountAsync(l => l.ItemId == item))).Should().Be(0);
        (await GetAsync(owner, orderId)).GetProperty("status").GetString().Should().Be("Approved");
        Planned(await PayablesAsync(DocumentOrigin.Purchase, orderId)).Should().Be(100m, "o previsto não mudou");
    }

    /// <summary>O contrato de produção com os títulos em aberto recusando (todos os membros delegam).</summary>
    private sealed class RefusingPayables(IPayables inner) : IPayables
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
            inner.CreatePlannedInstallmentsAsync(origin, originId, plan, cancellationToken);

        public Task<Result<IReadOnlyList<PayableRef>>> ReplacePlannedAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default) =>
            inner.ReplacePlannedAsync(origin, originId, plan, cancellationToken);

        public Task<Result> CancelAllFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default) =>
            inner.CancelAllFromOriginAsync(origin, originId, reason, cancellationToken);

        public Task<Result<IReadOnlyList<PayableRef>>> CreateOpenInstallmentsAsync(DocumentOrigin origin, Guid originId, OpenPayables plan, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<IReadOnlyList<PayableRef>>>(Error.Conflict("payables_unavailable", "Contas a pagar indisponível no teste."));

        public Task<IReadOnlyList<PayableRef>> ListByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) =>
            inner.ListByOriginAsync(origin, originId, cancellationToken);

        public Task<IReadOnlyList<ProjectPayable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ByProjectAsync(projectId, cancellationToken);

        public Task<IReadOnlyList<PayableOriginStatus>> StatusByOriginAsync(DocumentOrigin origin, IReadOnlyCollection<Guid> originIds, CancellationToken cancellationToken = default) => inner.StatusByOriginAsync(origin, originIds, cancellationToken);
    }
}
