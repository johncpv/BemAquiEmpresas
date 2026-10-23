using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Conclusão da OS de fabricação (PRD-06a1, D12, ADR 0023 §7): "Concluir OS" a partir de "Pronta para concluir" e "Estornar conclusão",
/// com <c>production.approve</c> (endpoint) numa sessão completa. Nada entra no estoque aqui (PRD-06a2). O projeto "Fabricado" e a
/// parcela vêm pelo Commercial (<see cref="ProductionOrderCompleted"/> → <c>ProjectFabricated</c>, ADR 0019 §1).
/// </summary>
public sealed partial class FabricationOrderService
{
    public static Error NotReady(string blocker) => Error.Conflict("production_order_not_ready", blocker);

    /// <summary>Impacto do diálogo (CA 2, 4, 10 e 11): bloqueios, última OS do projeto e parcelas (valor e vencimento só com finance.view).</summary>
    public async Task<Result<OrderCompletionPreview>> CompletionPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await Fabrication(db.ProductionOrders.AsNoTracking()).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var blocker = await CompletionBlockerAsync(order, cancellationToken);
        var siblings = await Fabrication(db.ProductionOrders.AsNoTracking()).Where(o => o.ProjectId == order.ProjectId).Select(o => new { o.Id, o.Status }).ToListAsync(cancellationToken);
        var last = CompletionRules.IsLastOrder(order.Id, siblings.Select(o => (o.Id, o.Status)));
        var otherOpen = siblings.Count(o => o.Id != order.Id && o.Status is not (ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled));
        var project = await projects.FabricationAsync(order.ProjectId, cancellationToken);
        var showAmounts = await permissions.HasPermissionAsync("finance", "view", cancellationToken);
        var milestones = (project?.Milestones ?? []).Select(m => new CompletionMilestoneView(
            m.Description, showAmounts ? m.Amount : null, showAmounts ? Today.AddDays(m.DaysAfterEvent) : null, showAmounts ? m.ReceivableId : null, m.Released, m.Received)).ToList();
        var reverseBlocker = order.Status != ProductionOrderStatus.Completed ? "Só OS concluída tem a conclusão estornada." : project?.ReversalBlocker;
        return new OrderCompletionPreview(
            order.Id, order.Number, order.Status, blocker is null, blocker, reverseBlocker is null, reverseBlocker, last, otherOpen, order.ProjectId, project?.Number ?? string.Empty,
            project?.Status ?? string.Empty, project?.FabricatedByOrders == true, showAmounts, milestones, CompletionRules.StockNotice, order.Version);
    }

    /// <summary>
    /// "Concluir OS" (CA 2 a 5): a OS "Pronta para concluir" vai a <c>Completed</c> (<c>actual_end</c>, <c>completed_by</c>) com a quantidade
    /// inteira e o <see cref="ProductionOrderCompleted"/> no outbox, na mesma transação; fora disso, 409 com o motivo. Duas conclusões juntas,
    /// só uma grava (xmin, CA 14).
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

        var now = clock.GetUtcNow();
        order.Status = ProductionOrderStatus.Completed;
        order.ActualEnd = now;
        order.CompletedBy = currentUser.UserId;
        events.Publish(new ProductionOrderCompleted(order.Id, order.ProjectId, order.Quantity.GetValueOrDefault(), BusinessTime.Today(clock)));
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Estornar conclusão" (CA 10 e 11): só OS <c>Completed</c>, com motivo; recusado (409) quando a parcela "fabricação concluída" do projeto
    /// fabricado pelas OS já tem baixa ou o projeto passou da fabricação, conferido de forma síncrona no Commercial antes de gravar. A última
    /// etapa volta a <c>Ready</c>, a OS a <c>InProgress</c> e o <see cref="ProductionOrderCompletionReversed"/> vai para o outbox. O título a
    /// receber não é estornado.
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
        }

        var now = clock.GetUtcNow();
        order.Status = CompletionRules.ReversedStatus(steps.Select(s => new StepState(s.Status, s.IsQualityStep)).ToList());
        order.ActualEnd = null;
        order.CompletedBy = null;
        order.CompletionReversedAt = now;
        order.CompletionReversedBy = currentUser.UserId;
        order.CompletionReversalReason = reason;
        events.Publish(new ProductionOrderCompletionReversed(order.Id, order.ProjectId, BusinessTime.Today(clock)));
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
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