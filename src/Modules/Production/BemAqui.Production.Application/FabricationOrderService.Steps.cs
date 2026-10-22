using BemAqui.Engineering.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class FabricationOrderService
{
    /// <summary>
    /// "Salvar etapas" (CA 7 e 8): só OS aberta sem etapa iniciada; grava na OS (o template não muda) com as regras do roteiro;
    /// a primeira fica pronta e as demais aguardando. Etapa com id é desta OS (IDOR, CA 23); centro pelo <c>IWorkCenters</c>.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> SaveStepsAsync(Guid id, FabricationStepsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadEditableAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (order, current) = loaded.Value;
        var (steps, errors) = FabricationRules.Validate(input.Steps);
        var known = current.ToDictionary(s => s.Id);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is { } stepId && !known.ContainsKey(stepId))
            {
                return Error.NotFound("step_not_found", "Etapa não encontrada nesta OS.");
            }
        }

        var centerIds = steps.Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value).Distinct().ToList();
        var centers = (await workCenters.FindAsync(centerIds, cancellationToken)).ToDictionary(c => c.Id);
        for (var i = 0; i < steps.Count; i++)
        {
            // Centro inativo só continua na etapa que já o tinha.
            if (steps[i].WorkCenterId is { } center && (!centers.TryGetValue(center, out var found)
                || (!found.IsActive && !(steps[i].Id is { } sid && known[sid].WorkCenterId == center))))
            {
                errors.TryAdd($"steps[{i}].workCenterId", ["Escolha um centro de trabalho ativo."]);
            }
        }

        if (FabricationRules.ToError(errors) is { } invalid)
        {
            return invalid;
        }

        await ReplaceStepsAsync(order, current, steps, cancellationToken);
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Roteiros ativos para o "Trocar roteiro" (CA 9): etapas e horas de cada um e o "Sugerido" (CA 4).</summary>
    public async Task<Result<IReadOnlyList<FabricationRoutingOption>>> RoutingOptionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await Fabrication(db.ProductionOrders.AsNoTracking()).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var item = await projects.FindItemsAsync([order.ProjectItemId.GetValueOrDefault()], cancellationToken) is [var found, ..] ? found : null;
        var suggested = await SuggestAsync(item?.RoutingTemplateId, order.ProductId, cancellationToken);
        return (await routings.ActiveAsync(cancellationToken))
            .Select(t => new FabricationRoutingOption(t.Id, t.Name, t.ProductType, t.Steps.Count, t.Steps.Sum(s => s.StandardHours), t.Id == suggested?.Id, t.Steps.All(s => s.WorkCenterId is not null)))
            .OrderByDescending(o => o.Suggested).ThenBy(o => o.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>"Trocar roteiro" (CA 9): as etapas atuais dão lugar às do roteiro escolhido, só com a OS aberta e sem etapa iniciada.</summary>
    public async Task<Result<FabricationOrderDetail>> ChangeRoutingAsync(Guid id, FabricationRoutingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadEditableAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (order, current) = loaded.Value;
        if ((await routings.FindAsync([input.RoutingTemplateId], cancellationToken)) is not [{ IsActive: true } routing])
        {
            return InstallationTemplateService.Invalid("routingTemplateId", "Escolha um roteiro ativo.");
        }

        order.RoutingTemplateId = routing.Id;
        await ReplaceStepsAsync(order, current, routing.Steps.Select(s => new FabricationStepDraft(null, s.Sequence, s.Name, s.Code, s.WorkCenterId, s.StandardHours, s.IsQualityStep,
            s.RequiresApproval, s.Checklist.OrderBy(c => c.Sequence).Select(c => c.Text).ToList())).ToList(), cancellationToken);
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Substitui as etapas: a etapa com id é atualizada no lugar (histórico por etapa), a que saiu é removida e a nova entra; o
    /// checklist é regravado linha a linha. Toca a OS para a versão (xmin) mudar.
    /// </summary>
    private async Task ReplaceStepsAsync(ProductionOrder order, List<ProductionOrderStep> current, IReadOnlyList<FabricationStepDraft> steps, CancellationToken ct)
    {
        var checklist = await db.ProductionOrderChecklist.Where(c => c.OrderId == order.Id && c.StepId != null).ToListAsync(ct);
        db.ProductionOrderChecklist.RemoveRange(checklist);
        var keep = steps.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        db.ProductionOrderSteps.RemoveRange(current.Where(s => !keep.Contains(s.Id)));
        var byId = current.ToDictionary(s => s.Id);
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            ProductionOrderStep step;
            if (s.Id is { } existing)
            {
                step = byId[existing];
            }
            else
            {
                step = new ProductionOrderStep { OrderId = order.Id };
                db.ProductionOrderSteps.Add(step);
            }

            step.Sequence = i + 1;
            step.Name = s.Name;
            step.Code = s.Code;
            step.WorkCenterId = s.WorkCenterId;
            step.StandardHours = s.StandardHours;
            step.IsQualityStep = s.IsQualityStep;
            step.RequiresApproval = s.RequiresApproval;
            step.Status = FabricationRules.InitialStatus(i);
            for (var n = 0; n < s.Checklist.Count; n++)
            {
                db.ProductionOrderChecklist.Add(new ProductionOrderChecklistItem { OrderId = order.Id, StepId = step.Id, Sequence = n + 1, Text = s.Checklist[n] });
            }
        }

        db.Entry(order).Property(o => o.Priority).IsModified = true;
    }

    /// <summary>OS de fabricação aberta, na versão que o cliente abriu, sem etapa iniciada (CA 8, 24).</summary>
    private async Task<Result<(ProductionOrder Order, List<ProductionOrderStep> Steps)>> LoadEditableAsync(Guid id, uint version, CancellationToken ct)
    {
        var loaded = await LoadAsync(id, version, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(ct);
        if (order.Status == ProductionOrderStatus.Cancelled || order.Status == ProductionOrderStatus.Completed)
        {
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não muda.");
        }

        if (FabricationRules.HasStarted(order.Status, steps.Select(s => s.Status)))
        {
            return Error.Conflict("production_order_started", "A OS já começou: as etapas não mudam.");
        }

        return (order, steps);
    }

    private async Task<Result<ProductionOrder>> LoadAsync(Guid id, uint version, CancellationToken ct)
    {
        var order = await Fabrication(db.ProductionOrders).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        if (order.Version != version)
        {
            return InstallationOrderService.Stale();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = version;
        return order;
    }

    /// <summary>
    /// "Cancelar OS" (CA 16 e 17): a aberta sem etapa iniciada com <c>production.edit</c>; a iniciada só com <c>production.approve</c>.
    /// Publica <see cref="ProductionOrderCancelled"/> para a EST-02 reconciliar as reservas pela fonte.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> CancelAsync(Guid id, FabricationCancelInput input, CancellationToken cancellationToken = default)
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
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não é cancelada.");
        }

        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == id).Select(s => s.Status).ToListAsync(cancellationToken);
        if (FabricationRules.HasStarted(order.Status, steps) && !await permissions.HasPermissionAsync("production", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "OS iniciada só é cancelada por quem aprova na produção.");
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return InstallationTemplateService.Invalid("reason", "Informe o motivo do cancelamento, com até 500 caracteres.");
        }

        Cancel(order, reason);
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Contrato cancelado (CA 18, handler próprio da fabricação): cancela as OS abertas sem etapa iniciada e avisa o responsável
    /// do projeto das iniciadas, para decisão manual.
    /// </summary>
    internal async Task CancelForContractAsync(Guid projectId, CancellationToken ct)
    {
        var orders = await Fabrication(db.ProductionOrders).Where(o => o.ProjectId == projectId && o.Status != ProductionOrderStatus.Completed && o.Status != ProductionOrderStatus.Cancelled)
            .ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => ids.Contains(s.OrderId)).Select(s => new { s.OrderId, s.Status }).ToListAsync(ct)).ToLookup(s => s.OrderId, s => s.Status);
        var started = new List<ProductionOrder>();
        foreach (var order in orders)
        {
            if (FabricationRules.HasStarted(order.Status, steps[order.Id]))
            {
                started.Add(order);
            }
            else
            {
                Cancel(order, "Contrato cancelado.");
                await board.ChangedAsync(order.Id, ct);
            }
        }

        if (started.Count > 0 && await projects.FindAsync(projectId, ct) is { ManagerUserId: { } manager })
        {
            foreach (var order in started)
            {
                notifications.Notify(manager, "production.fabrication_contract_cancelled", $"Contrato cancelado com a OS {order.Number} iniciada: decida se continua ou cancela.", Link + order.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private void Cancel(ProductionOrder order, string reason)
    {
        order.Status = ProductionOrderStatus.Cancelled;
        order.CancelReason = reason;
        order.CancelledAt = clock.GetUtcNow();
        order.CancelledBy = currentUser.UserId;
        events.Publish(new ProductionOrderCancelled(order.Id, order.ProjectId));
    }
}