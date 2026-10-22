using BemAqui.Commercial.Contracts;
using BemAqui.Engineering.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class FabricationOrderService
{
    private const int MaxCandidates = 5000;

    /// <summary>
    /// "Gerar OS de fabricação" (CA 5 e 6): equipamento da Fase 2 ou componente fabricado, sem OS não cancelada; serviço,
    /// implantação e projeto já fabricado são recusados.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> GenerateAsync(FabricationGenerateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if ((await projects.FindItemsAsync([input.ProjectItemId], cancellationToken)) is not [var item])
        {
            return Error.NotFound("project_item_not_found", "Item do projeto não encontrado.");
        }

        var project = await projects.FindAsync(item.ProjectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("project_not_found", "Projeto não encontrado.");
        }

        if (FabricationRules.CanGenerate(item, project) is { } refused)
        {
            return refused;
        }

        if (await OpenOrderNumberAsync(item.Id, cancellationToken) is { } existing)
        {
            return Error.Conflict("project_item_has_order", $"O item {item.Description} já está na {existing}.");
        }

        ProductionOrder order;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            order = await CreateAsync(item, project, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Error.Conflict("project_item_has_order", $"O item {item.Description} já tem OS de fabricação. Recarregue.");
            }

            await NotifyNoRoutingAsync(order, project, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(order.Id, cancellationToken);
    }

    /// <summary>"Gerar OS" em lote (CA 5): uma OS por item escolhido; o que não pode gerar fica de fora com o motivo.</summary>
    public async Task<Result<FabricationBatchResult>> GenerateManyAsync(FabricationBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ids = (input.ProjectItemIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > 200)
        {
            return InstallationTemplateService.Invalid("projectItemIds", "Escolha de 1 a 200 itens.");
        }

        var found = (await projects.FindItemsAsync(ids, cancellationToken)).ToDictionary(i => i.Id);
        var byProject = (await projects.FindManyAsync(found.Values.Select(i => i.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var covered = await CoveredAsync(ids, cancellationToken);
        var skipped = new List<FabricationBatchSkip>();
        var created = new List<(ProductionOrder Order, ProjectSummary Project)>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var item) || !byProject.TryGetValue(item.ProjectId, out var project))
            {
                skipped.Add(new FabricationBatchSkip(id, "Item do projeto não encontrado."));
            }
            else if (FabricationRules.CanGenerate(item, project) is { } refused)
            {
                skipped.Add(new FabricationBatchSkip(id, refused.Message));
            }
            else if (covered.TryGetValue(id, out var number))
            {
                skipped.Add(new FabricationBatchSkip(id, $"O item {item.Description} já está na {number}."));
            }
            else
            {
                created.Add((await CreateAsync(item, project, cancellationToken), project));
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Error.Conflict("project_item_has_order", "Algum item ganhou OS enquanto você escolhia. Recarregue a lista.");
        }

        foreach (var (order, project) in created)
        {
            await NotifyNoRoutingAsync(order, project, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new FabricationBatchResult(created.Count, created.Select(c => c.Order.Number).ToList(), skipped);
    }

    /// <summary>Filtro "Itens de projeto sem OS" (CA 5): equipamento e componente de projeto aberto, sem OS de fabricação não cancelada.</summary>
    public async Task<PendingFabricationPage> PendingAsync(Guid? projectId, BemAqui.BuildingBlocks.Paging.PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var candidates = await projects.FabricationCandidatesAsync(projectId, MaxCandidates, cancellationToken);
        var covered = await CoveredAsync(candidates.Select(i => i.Id).ToList(), cancellationToken);
        var pending = candidates.Where(i => !covered.ContainsKey(i.Id)).ToList();
        var shown = pending.Skip(page.Skip).Take(page.PageSize).ToList();
        var byProject = (await projects.FindManyAsync(shown.Select(i => i.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var names = (await parties.FindManyAsync(byProject.Values.Select(p => p.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var products = (await items.FindAsync(shown.Where(i => i.ItemId is not null).Select(i => i.ItemId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        return new PendingFabricationPage(shown.Select(i =>
        {
            var project = byProject.GetValueOrDefault(i.ProjectId);
            return new PendingFabricationItem(
                i.Id, i.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, project is null ? string.Empty : names.GetValueOrDefault(project.PartyId, string.Empty),
                i.Sequence, i.Description, i.Quantity, i.Kind, i.ItemId, i.ItemId is { } p ? products.GetValueOrDefault(p)?.Name : null, project?.PromisedDate);
        }).ToList(), page.Page, page.PageSize, pending.Count);
    }

    /// <summary>
    /// Consumidor do <c>ProjectItemCreated</c> (CA 1 a 4): só o equipamento de contrato que não é de implantação; idempotente pela
    /// inbox e pelo item (índice único parcial). Roda na transação do consumidor.
    /// </summary>
    internal async Task GenerateFromEventAsync(ProjectItemCreated created, CancellationToken ct)
    {
        if (FabricationRules.FromEvent(created.Kind, created.IsOnboarding) != FabricationDecision.Generate || await OpenOrderNumberAsync(created.ProjectItemId, ct) is not null)
        {
            return;
        }

        var project = await projects.FindAsync(created.ProjectId, ct);
        if (project is null || project.Status is not ("Planned" or "InProgress"))
        {
            return;
        }

        var item = new ProjectItemInfo(created.ProjectItemId, created.ProjectId, 0, string.Empty, created.Quantity, created.ItemId, created.Kind!.Value, false, created.RoutingTemplateId);
        var order = await CreateAsync(item, project, ct);
        await db.SaveChangesAsync(ct);
        await NotifyNoRoutingAsync(order, project, ct);
    }

    /// <summary>Monta a OS (número na transação aberta, etapas copiadas do roteiro sugerido); grava no SaveChanges de quem chama.</summary>
    private async Task<ProductionOrder> CreateAsync(ProjectItemInfo item, ProjectSummary project, CancellationToken ct)
    {
        var order = new ProductionOrder
        {
            Number = await numbers.NextAsync(InstallationOrderService.DocType, ct), Type = ProductionOrderType.Fabrication, ProjectId = project.Id, ContractId = project.ContractId,
            ProjectItemId = item.Id, ProductId = item.ItemId, Quantity = item.Quantity, Priority = 0, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.ProductionOrders.Add(order);
        await board.ChangedAsync(order.Id, ct); // sai no commit de quem chama (PRD-02 CA 12)
        if (await SuggestAsync(item.RoutingTemplateId, item.ItemId, ct) is { } routing)
        {
            order.RoutingTemplateId = routing.Id;
            AddSteps(order, routing.Steps.Select(s => new FabricationStepDraft(null, s.Sequence, s.Name, s.Code, s.WorkCenterId, s.StandardHours, s.IsQualityStep, s.RequiresApproval,
                s.Checklist.OrderBy(c => c.Sequence).Select(c => c.Text).ToList())).ToList());
        }

        return order;
    }

    /// <summary>Roteiro sugerido (CA 4): item do projeto, produto, último usado com o produto e o único ativo.</summary>
    private async Task<RoutingTemplateInfo?> SuggestAsync(Guid? itemRouting, Guid? productId, CancellationToken ct)
    {
        var active = await routings.ActiveAsync(ct);
        if (active.Count == 0)
        {
            return null;
        }

        Guid? byProduct = null;
        Guid? lastUsed = null;
        if (productId is { } product)
        {
            byProduct = (await routings.ForItemAsync(product, ct))?.Id;
            lastUsed = await Fabrication(db.ProductionOrders.AsNoTracking()).Where(o => o.ProductId == product && o.RoutingTemplateId != null)
                .OrderByDescending(o => o.CreatedAt).Select(o => o.RoutingTemplateId).FirstOrDefaultAsync(ct);
        }

        var chosen = FabricationRules.PickRouting(itemRouting, byProduct, lastUsed, active.Select(t => t.Id).ToList());
        return chosen is { } id ? active.First(t => t.Id == id) : null;
    }

    /// <summary>Copia as etapas para a OS (nada aponta para o template): a primeira pronta, as demais aguardando; checklist linha a linha.</summary>
    private void AddSteps(ProductionOrder order, List<FabricationStepDraft> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var step = new ProductionOrderStep
            {
                OrderId = order.Id, Sequence = i + 1, Name = s.Name, Code = s.Code, WorkCenterId = s.WorkCenterId, StandardHours = s.StandardHours, IsQualityStep = s.IsQualityStep,
                RequiresApproval = s.RequiresApproval, Status = FabricationRules.InitialStatus(i),
            };
            db.ProductionOrderSteps.Add(step);
            for (var n = 0; n < s.Checklist.Count; n++)
            {
                db.ProductionOrderChecklist.Add(new ProductionOrderChecklistItem { OrderId = order.Id, StepId = step.Id, Sequence = n + 1, Text = s.Checklist[n] });
            }
        }
    }

    /// <summary>OS sem roteiro (CA 4, R-PRP-5): quem edita a produção recebe o aviso com o link, uma vez por OS.</summary>
    private async Task NotifyNoRoutingAsync(ProductionOrder order, ProjectSummary project, CancellationToken ct)
    {
        if (order.RoutingTemplateId is not null)
        {
            return;
        }

        foreach (var user in await users.UsersWithPermissionAsync("production.edit", ct))
        {
            await notifications.NotifyOnceAsync(user, "production.order_no_routing", $"OS {order.Number} de {project.Number} sem roteiro.", Link + order.Id, $"os-no-routing:{order.Id}", ct);
        }
    }

    /// <summary>Número da OS de fabricação não cancelada do item, ou null.</summary>
    private async Task<string?> OpenOrderNumberAsync(Guid projectItemId, CancellationToken ct) =>
        await Fabrication(db.ProductionOrders.AsNoTracking()).Where(o => o.ProjectItemId == projectItemId && o.Status != ProductionOrderStatus.Cancelled)
            .Select(o => o.Number).FirstOrDefaultAsync(ct);

    /// <summary>Item do projeto → número da OS de fabricação não cancelada.</summary>
    private async Task<Dictionary<Guid, string>> CoveredAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken ct) =>
        await Fabrication(db.ProductionOrders.AsNoTracking()).Where(o => o.ProjectItemId != null && projectItemIds.Contains(o.ProjectItemId.Value) && o.Status != ProductionOrderStatus.Cancelled)
            .ToDictionaryAsync(o => o.ProjectItemId!.Value, o => o.Number, ct);

    public const string ItemIndex = "ux_production_orders_fabrication_item";

    private static bool IsUniqueViolation(DbUpdateException ex) => BemAqui.BuildingBlocks.Persistence.DbErrors.IsUniqueViolation(ex, ItemIndex);
}