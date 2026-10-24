using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Produto acabado lançado na conclusão (CA 5): o movimento, as séries, o local e o custo unitário, para o evento e o histórico.</summary>
public sealed record FinishedGoodsPosted(Guid MovementId, Guid ProductId, Guid LocationId, decimal UnitCost, IReadOnlyList<string> SerialNumbers);

/// <summary>
/// Produto acabado na conclusão da OS (PRD-06a2, ADR 0023 §1): a Produção é dona da transação e chama o Inventory de forma síncrona,
/// só pelos contratos (<see cref="IStockMovements"/>, <see cref="IStockSerials"/>, <see cref="IStockTraceability"/> e
/// <see cref="IStockReservations"/>). O custo vem das fontes síncronas (ADR 0025 §2): as horas da OS e o
/// <see cref="IStockMovements.CostByOrderAsync"/>, nunca da razão do Commercial, e não depende da permissão de quem conclui.
/// </summary>
public sealed class FinishedGoods(
    IProductionDb db, IStockMovements stock, IStockSerials serials, IStockTraceability traceability, IStockReservations reservations, ILocations locations, IItems items,
    IPermissionChecker permissions, TenantContextHolder tenant)
{
    public const string InventoryModule = "inventory";

    /// <summary>O módulo Estoque liberado no tenant: sem ele, a OS conclui sem estoque (como na PRD-06a1).</summary>
    public bool Enabled => tenant.Resolved?.IsReleased(InventoryModule) == true;

    /// <summary>Custo apurado da OS agora (CA 3): Σ custo dos apontamentos + material baixado − devolvido.</summary>
    public async Task<OrderCost> CostAsync(ProductionOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var labor = await db.LaborEntries.AsNoTracking().Where(e => e.OrderId == order.Id).SumAsync(e => e.Cost, ct);
        var material = (await stock.CostByOrderAsync(order.Id, ct)).Total;
        return CompletionRules.Cost(labor, material, order.Quantity.GetValueOrDefault(1m) <= 0 ? 1m : order.Quantity.GetValueOrDefault(1m));
    }

    /// <summary>"Escolha o item de estoque do produto antes de concluir." quando o Estoque está liberado e a OS não tem produto (CA 1).</summary>
    public string? ProductBlocker(ProductionOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return Enabled && order.ProductId is null ? CompletionRules.ProductRequired : null;
    }

    /// <summary>Impacto no estoque para o diálogo (CA 2, 4, 7 e 10).</summary>
    public async Task<CompletionStockView> PreviewAsync(ProductionOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var quantity = order.Quantity.GetValueOrDefault();
        if (!Enabled)
        {
            return new CompletionStockView(false, order.ProductId, null, null, null, quantity, false, false, [], null, [], [], false, null, null, null, null, false, null, null, 0, 0,
                order.CompletedWithoutStock);
        }

        var product = order.ProductId is { } productId && await items.FindAsync([productId], ct) is [var found, ..] ? found : null;
        var active = await locations.ActiveAsync(ct);
        var suggested = DefaultLocation(active);
        var units = product?.TracksSerial == true ? SerialRules.Units(quantity) : null;
        var suggestedSerials = order.Status != ProductionOrderStatus.Completed && product is { TracksSerial: true } && units is { } count
            ? await serials.SuggestAsync(product.Id, count, ct) : [];
        var toRelease = await ReservationsAsync(order.Id, ct);
        var showCost = await permissions.HasPermissionAsync("projects", "view_cost", ct);
        var cost = await CostAsync(order, ct);
        var orderSerials = await serials.ForOrderAsync(order.Id, ct);
        var entry = order.EntryMovementId is not null ? await serials.EntryAsync(order.Id, ct) : null;
        return new CompletionStockView(
            true, product?.Id, product?.Code, product?.Name, product?.UnitCode, quantity, product?.TracksSerial == true, product?.TracksLot == true, suggestedSerials, suggested?.Id,
            active.Select(l => new CompletionLocationOption(l.Id, l.Code, l.Name, l.IsDefault)).ToList(), toRelease, showCost, showCost ? cost.Labor : null,
            showCost ? cost.Material : null, showCost ? cost.Total : null, showCost ? cost.Unit : null, cost.Total == 0, entry?.MovementId, entry?.Number,
            orderSerials.Count(s => s.Status == SerialState.InStock), orderSerials.Count(s => s.Status == SerialState.Shipped), order.CompletedWithoutStock);
    }

    /// <summary>
    /// Confere o pedido antes da transação (CA 1, 4 e 7): produto, local ativo, séries por unidade e a confirmação do custo zerado. Devolve
    /// o local escolhido, o custo e o produto.
    /// </summary>
    public async Task<Result<(ItemSummary Product, Guid LocationId, OrderCost Cost, int? Units)>> ValidateAsync(ProductionOrder order, CompleteOrderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(input);
        if (order.ProductId is not { } productId)
        {
            return Error.Validation("product_required", CompletionRules.ProductRequired, new Dictionary<string, string[]> { ["productId"] = [CompletionRules.ProductRequired] });
        }

        if (await items.FindAsync([productId], ct) is not [var product, ..] || product.IsDeleted || product.Type == ItemType.Service)
        {
            return Field("productId", "O item do produto não existe mais ou é um serviço. Escolha outro item de estoque.");
        }

        var active = await locations.ActiveAsync(ct);
        var location = input.LocationId is { } chosen ? active.FirstOrDefault(l => l.Id == chosen) : DefaultLocation(active);
        if (location is null)
        {
            return Field("locationId", input.LocationId is null ? "Cadastre um local de estoque antes de concluir." : "Local não encontrado ou inativo.");
        }

        int? units = null;
        if (product.TracksSerial)
        {
            units = SerialRules.Units(order.Quantity.GetValueOrDefault());
            if (units is null)
            {
                return Field("serialNumbers", "Produto com série entra em unidades inteiras. Corrija a quantidade do item do projeto.");
            }

            if (input.SerialNumbers is { } typed && SerialRules.Invalid(typed, units.Value) is { } invalid)
            {
                return Field("serialNumbers", invalid);
            }
        }
        else if (input.SerialNumbers is { Count: > 0 })
        {
            return Field("serialNumbers", "Este produto não controla série: a entrada é por quantidade.");
        }

        var cost = await CostAsync(order, ct);
        if (cost.Total == 0 && !input.ConfirmZeroCost)
        {
            return Field("confirmZeroCost", CompletionRules.ZeroCostWarning + " Confirme para concluir.");
        }

        return (product, location.Id, cost, units);
    }

    /// <summary>
    /// Na transação da conclusão (CA 5 e 8): libera as reservas restantes (alvo 0), lança a entrada <c>Production</c> pelo custo apurado,
    /// grava as séries e o vínculo dos lotes consumidos. Qualquer falha devolve o erro e o chamador desfaz tudo.
    /// </summary>
    public async Task<Result<FinishedGoodsPosted>> PostAsync(
        ProductionOrder order, ItemSummary product, Guid locationId, OrderCost cost, int? units, IReadOnlyList<string>? serialNumbers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(product);
        await reservations.ReleaseForOrderAsync(order.Id, ct);
        var last = await serials.EntryAsync(order.Id, ct);
        var lot = product.TracksLot ? await stock.EnsureLotAsync(product.Id, order.Number, cancellationToken: ct) : (Guid?)null;
        var posted = await stock.PostAsync(new StockPosting(
            StockMovementType.Production, product.Id, locationId, lot, order.Quantity.GetValueOrDefault(), cost.Unit, StockSources.ProductionOrder, order.Id,
            CompletionRules.EntryLeg(last?.Leg, last?.Reversed == true), ProjectId: order.ProjectId, ProductionOrderId: order.Id, SourceNumber: order.Number), ct);
        if (posted.IsFailure)
        {
            return posted.Error!;
        }

        IReadOnlyList<string> numbers = [];
        if (units is { } count)
        {
            var created = await serials.CreateAsync(new SerialCreate(product.Id, count, serialNumbers, order.Id, order.ProjectId, locationId, posted.Value.MovementId), ct);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            numbers = created.Value.Select(s => s.SerialNumber).ToList();
        }

        await traceability.RecordOrderAsync(order.Id, ct);
        return new FinishedGoodsPosted(posted.Value.MovementId, product.Id, locationId, cost.Unit, numbers);
    }

    /// <summary>
    /// Na transação do estorno (CA 10): série expedida bloqueia (409 "O produto já foi expedido."); as demais vão a <c>Cancelled</c>, a
    /// entrada ganha o movimento inverso (sem saldo: 409 <c>insufficient_stock</c>) e o vínculo de lotes fica estornado. A reserva é
    /// reconciliada pela fonte depois, pelo evento do estorno (ADR 0023 §6).
    /// </summary>
    public async Task<Result> ReverseAsync(ProductionOrder order, string reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.EntryMovementId is not { } movement)
        {
            return Result.Success();
        }

        var cancelled = await serials.CancelForOrderAsync(order.Id, ct);
        if (cancelled.IsFailure)
        {
            return cancelled.Error!;
        }

        var reversed = await stock.ReverseAsync(new StockReversal(movement, StockSources.ProductionOrder, order.Id, "Estorno da conclusão: " + reason), ct);
        if (reversed.IsFailure)
        {
            return reversed.Error!;
        }

        await traceability.ReverseOrderAsync(order.Id, ct);
        return Result.Success();
    }

    /// <summary>Itens que podem ser o produto da OS (CA 1): produto acabado ou componente ativo, pela busca.</summary>
    public async Task<IReadOnlyList<OrderProductOption>> ProductOptionsAsync(string? term, CancellationToken ct)
    {
        var finished = await items.SearchAsync(ItemType.Finished, term, 20, ct);
        var components = await items.SearchAsync(ItemType.Component, term, 20, ct);
        return finished.Concat(components).Where(i => i.IsActive && !i.IsDeleted).OrderBy(i => i.Code)
            .Select(i => new OrderProductOption(i.Id, i.Code, i.Name, i.Type.ToString(), i.TracksSerial)).Take(20).ToList();
    }

    private async Task<IReadOnlyList<CompletionReservationView>> ReservationsAsync(Guid orderId, CancellationToken ct)
    {
        var active = (await reservations.ForOrderAsync(orderId, ct)).Where(r => r.Status == ReservationState.Active && r.Reserved > 0).ToList();
        if (active.Count == 0)
        {
            return [];
        }

        var names = (await items.FindAsync(active.Select(r => r.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var places = (await locations.FindAsync(active.Select(r => r.LocationId).Distinct().ToList(), ct)).ToDictionary(l => l.Id, l => l.Name);
        return active.Select(r =>
        {
            var item = names.GetValueOrDefault(r.ItemId);
            return new CompletionReservationView(r.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.UnitCode ?? string.Empty, places.GetValueOrDefault(r.LocationId, string.Empty), r.Reserved);
        }).OrderBy(r => r.ItemCode).ToList();
    }

    /// <summary>Local sugerido (padrão 6): o padrão do tenant (o item não tem local próprio); sem ele, o primeiro ativo.</summary>
    private static LocationOption? DefaultLocation(IReadOnlyList<LocationOption> active) =>
        active.FirstOrDefault(l => l.IsDefault) ?? (active.Count > 0 ? active[0] : null);

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
