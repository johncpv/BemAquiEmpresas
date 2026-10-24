using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Conclusão da OS de fabricação (PRD-06a1, D12, ADR 0023 §7): "Concluir OS" a partir de "Pronta para concluir" e "Estornar conclusão",
/// com <c>production.approve</c> (endpoint) numa sessão completa. PRD-06a2: na mesma transação, o produto acabado entra no estoque pelo
/// custo apurado da OS, com as séries, os lotes consumidos e as reservas restantes liberadas (<see cref="FinishedGoods"/>, ADR 0023 §1); o
/// estorno desfaz a entrada. O projeto "Fabricado" e a parcela vêm pelo Commercial (<see cref="ProductionOrderCompleted"/> →
/// <c>ProjectFabricated</c>, ADR 0019 §1).
/// </summary>
public sealed partial class FabricationOrderService
{
    public static Error NotReady(string blocker) => Error.Conflict("production_order_not_ready", blocker);

    /// <summary>
    /// Impacto do diálogo (PRD-06a1 CA 2, 4, 10 e 11; PRD-06a2 CA 2, 3, 4, 7 e 10): bloqueios, última OS do projeto, parcelas (valor e
    /// vencimento só com finance.view) e o impacto no estoque (custo só com projects.view_cost).
    /// </summary>
    public async Task<Result<OrderCompletionPreview>> CompletionPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await Fabrication(db.ProductionOrders.AsNoTracking()).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var blocker = await CompletionBlockerAsync(order, cancellationToken) ?? goods.ProductBlocker(order);
        var siblings = await Fabrication(db.ProductionOrders.AsNoTracking()).Where(o => o.ProjectId == order.ProjectId).Select(o => new { o.Id, o.Status }).ToListAsync(cancellationToken);
        var last = CompletionRules.IsLastOrder(order.Id, siblings.Select(o => (o.Id, o.Status)));
        var otherOpen = siblings.Count(o => o.Id != order.Id && o.Status is not (ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled));
        var project = await projects.FabricationAsync(order.ProjectId, cancellationToken);
        var showAmounts = await permissions.HasPermissionAsync("finance", "view", cancellationToken);
        var milestones = (project?.Milestones ?? []).Select(m => new CompletionMilestoneView(
            m.Description, showAmounts ? m.Amount : null, showAmounts ? Today.AddDays(m.DaysAfterEvent) : null, showAmounts ? m.ReceivableId : null, m.Released, m.Received)).ToList();
        var stock = await goods.PreviewAsync(order, cancellationToken);
        var reverseBlocker = order.Status != ProductionOrderStatus.Completed ? "Só OS concluída tem a conclusão estornada."
            : stock.ShippedSerials > 0 ? CompletionRules.ProductShipped : project?.ReversalBlocker;
        return new OrderCompletionPreview(
            order.Id, order.Number, order.Status, blocker is null, blocker, reverseBlocker is null, reverseBlocker, last, otherOpen, order.ProjectId, project?.Number ?? string.Empty,
            project?.Status ?? string.Empty, project?.FabricatedByOrders == true, showAmounts, milestones, stock.Enabled ? null : CompletionRules.StockNotice, order.Version, stock);
    }

    /// <summary>
    /// "Concluir OS" (PRD-06a1 CA 2 a 5; PRD-06a2 CA 1 a 8): a OS "Pronta para concluir" vai a <c>Completed</c> (<c>actual_end</c>,
    /// <c>completed_by</c>) com a quantidade inteira e, com o Estoque liberado, na mesma transação: as reservas restantes liberadas, a entrada
    /// <c>Production</c> pelo custo apurado, as séries e os lotes consumidos; o <see cref="ProductionOrderCompleted"/> vai para o outbox.
    /// Qualquer falha desfaz tudo e a OS continua pronta para concluir. Duas conclusões juntas, só uma grava (xmin, PRD-06a1 CA 14 e
    /// PRD-06a2 CA 13).
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> CompleteOrderAsync(Guid id, CompleteOrderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (await CompletionBlockerAsync(order, cancellationToken) is { } blocker)
        {
            return NotReady(blocker);
        }

        var withStock = goods.Enabled;
        var plan = withStock ? await goods.ValidateAsync(order, input, cancellationToken) : null;
        if (plan is { IsFailure: true })
        {
            return plan.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        order.Status = ProductionOrderStatus.Completed;
        order.ActualEnd = now;
        order.CompletedBy = currentUser.UserId;
        order.CompletedWithoutStock = !withStock;
        // Grava a OS antes do estoque: duas conclusões simultâneas param no xmin e só uma lança a entrada (CA 13).
        await db.SaveChangesAsync(cancellationToken);
        FinishedGoodsPosted? posted = null;
        if (plan is { Value: var (product, locationId, cost, units) })
        {
            var stock = await goods.PostAsync(order, product, locationId, cost, units, input.SerialNumbers, cancellationToken);
            if (stock.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return stock.Error!;
            }

            posted = stock.Value;
            order.EntryMovementId = posted.MovementId;
        }

        events.Publish(new ProductionOrderCompleted(
            order.Id, order.ProjectId, order.Quantity.GetValueOrDefault(), BusinessTime.Today(clock), posted?.ProductId, posted?.UnitCost ?? 0m, posted?.SerialNumbers, posted?.LocationId));
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Estornar conclusão" (PRD-06a1 CA 10 e 11; PRD-06a2 CA 10): só OS <c>Completed</c>, com motivo; recusado (409) quando a parcela
    /// "fabricação concluída" do projeto fabricado pelas OS já tem baixa ou o projeto passou da fabricação, conferido de forma síncrona no
    /// Commercial antes de gravar. Na mesma transação, a entrada no estoque ganha o movimento inverso, as séries não expedidas são
    /// canceladas e os lotes consumidos ficam estornados (série expedida: 409 "O produto já foi expedido."; sem saldo: 409
    /// <c>insufficient_stock</c>). A última etapa volta a <c>Ready</c>, a OS a <c>InProgress</c> e o
    /// <see cref="ProductionOrderCompletionReversed"/> vai para o outbox (a reserva é reconciliada pela fonte). O título a receber não é estornado.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> ReverseCompletionAsync(Guid id, ReverseCompletionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = CompletionRules.ReversalReason(input.Reason);
        if (reason is null)
        {
            return InstallationTemplateService.Invalid("reason", $"Informe o motivo do estorno, com até {CompletionRules.MaxReversalReason} caracteres.");
        }

        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status != ProductionOrderStatus.Completed)
        {
            return Error.Conflict("production_order_not_completed", "Só OS concluída tem a conclusão estornada.");
        }

        if ((await projects.FabricationAsync(order.ProjectId, cancellationToken))?.ReversalBlocker is { } blocked)
        {
            return Error.Conflict("fabrication_reversal_blocked", blocked);
        }

        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        if (steps.Count > 0)
        {
            var last = steps[^1];
            last.Status = ProductionStepStatus.Ready;
            last.ActualEnd = null;
            last.CompletedAt = null;
            last.CompletedBy = null;
            last.AwaitingApprovalSince = null;
            last.CompletedBySupervisor = false;
            last.CompletionReason = null;
            if (last.IsQualityStep)
            {
                await ReopenQualityAsync(order, last, cancellationToken);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        order.Status = CompletionRules.ReversedStatus(steps.Select(s => new StepState(s.Status, s.IsQualityStep)).ToList());
        order.ActualEnd = null;
        order.CompletedBy = null;
        order.CompletionReversedAt = now;
        order.CompletionReversedBy = currentUser.UserId;
        order.CompletionReversalReason = reason;
        order.CompletedWithoutStock = false;
        // Grava a OS antes do estoque: dois estornos juntos param no xmin e só um lança o movimento inverso.
        await db.SaveChangesAsync(cancellationToken);
        var stock = await goods.ReverseAsync(order, reason, cancellationToken);
        if (stock.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return stock.Error!;
        }

        order.EntryMovementId = null;
        events.Publish(new ProductionOrderCompletionReversed(order.Id, order.ProjectId, BusinessTime.Today(clock)));
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Escolher item do produto" (PRD-06a2 CA 1): a OS sem item de estoque (ENG-02 ponto 26) ganha o produto, com <c>production.edit</c>,
    /// enquanto não está concluída nem cancelada. O item precisa ser um produto acabado ou componente ativo.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> SetProductAsync(Guid id, OrderProductInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        {
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não muda.");
        }

        if (await items.FindAsync([input.ItemId], cancellationToken) is not [var item, ..] || !item.IsActive || item.IsDeleted
            || item.Type is not (Inventory.Contracts.ItemType.Finished or Inventory.Contracts.ItemType.Component))
        {
            return InstallationTemplateService.Invalid("itemId", "Escolha um produto acabado ou componente ativo do estoque.");
        }

        order.ProductId = item.Id;
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Custo apurado da OS para o diálogo (PRD-06a2 CA 2 e 3), em DTO separado, só com <c>projects.view_cost</c> (endpoint): o mesmo
    /// cálculo que a entrada usa, pelas fontes síncronas.
    /// </summary>
    public async Task<Result<OrderCost>> CompletionCostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await Fabrication(db.ProductionOrders.AsNoTracking()).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? InstallationOrderService.NotFound() : await goods.CostAsync(order, cancellationToken);
    }

    /// <summary>Itens que podem ser o produto da OS (CA 1), pela busca.</summary>
    public Task<IReadOnlyList<OrderProductOption>> ProductOptionsAsync(string? term, CancellationToken cancellationToken = default) =>
        goods.ProductOptionsAsync(term, cancellationToken);

    /// <summary>
    /// Estorno com a última etapa de qualidade: a decisão da tentativa atual já está gravada (único por etapa e tentativa, PRD-05), então a
    /// etapa volta numa tentativa nova da OS para ser inspecionada de novo, com o checklist zerado (os itens marcados da tentativa anterior
    /// ficam guardados) e sem a última decisão. O início da tentativa não muda: a segregação continua olhando quem trabalhou nela.
    /// </summary>
    private async Task ReopenQualityAsync(ProductionOrder order, ProductionOrderStep step, CancellationToken ct)
    {
        var current = await db.ProductionOrderChecklist.Where(c => c.StepId == step.Id && c.Attempt == step.Attempt).OrderBy(c => c.Sequence).ToListAsync(ct);
        order.QualityAttempt++;
        foreach (var item in current)
        {
            db.ProductionOrderChecklist.Add(new ProductionOrderChecklistItem { OrderId = item.OrderId, StepId = item.StepId, Sequence = item.Sequence, Text = item.Text, Attempt = order.QualityAttempt });
        }

        step.Attempt = order.QualityAttempt;
        step.DecidedBy = null;
        step.DecidedAt = null;
        step.SelfInspection = false;
    }

    /// <summary>Motivo que impede concluir a OS (CA 4), pelas etapas e pelo apontamento aberto; null: pode concluir.</summary>
    private async Task<string?> CompletionBlockerAsync(ProductionOrder order, CancellationToken ct)
    {
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == order.Id).OrderBy(s => s.Sequence)
            .Select(s => new CompletionStep(s.Name, s.Status, s.IsQualityStep, s.AwaitingApprovalSince != null)).ToListAsync(ct);
        var open = await (from e in db.LaborEntries.AsNoTracking()
                          join s in db.ProductionOrderSteps.AsNoTracking() on e.OrderStepId equals s.Id
                          where e.OrderId == order.Id && e.EndedAt == null
                          orderby s.Sequence
                          select s.Name).FirstOrDefaultAsync(ct);
        return CompletionRules.Blocker(order.Status, steps, open);
    }
}
