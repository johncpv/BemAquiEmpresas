using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Pedido de venda (VND-02a): criar, editar, confirmar (reserva tudo ou nada), cobrar (título <c>SalesOrder</c> e boleto pelo Financeiro), registrar
/// pagamento, cancelar, reabrir o pago depois de cancelado, devolver o valor, e a conversão na venda da VND-01 (mesmo serviço da ingestão). Toda
/// transição passa pela máquina (<see cref="SalesOrderRules"/>) e pelo <c>xmin</c> do pedido, grava a linha do tempo e publica o evento no outbox.
/// </summary>
public sealed partial class SalesOrderService(
    ISalesDb db,
    SaleService sales,
    ISellableProducts products,
    ICatalogLookup lookup,
    IStockBalances balances,
    ISalesReservations reservations,
    ILocations locations,
    IParties parties,
    IOrderCharges charges,
    IPaymentMethods paymentMethods,
    SalesOrderParametersService parameters,
    INumberSequence numbers,
    IEventPublisher events,
    IPermissionChecker permissions,
    INotificationService notifications,
    IUserDirectory users,
    TimeProvider clock)
{
    public const string NumberIndex = "ux_orders_number";
    public const string ExternalIndex = "ux_orders_origin_app_external";
    public const string NotificationType = "sales.order";
    public const string ApprovalCode = "approval_required";
    public const string ApprovalMessage = "Desconto acima do limite: só quem aprova em Vendas pode confirmar.";
    public const string TransitionCode = "invalid_order_transition";
    public const string LockedMessage = "Cancele a cobrança para alterar o pedido.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Link(Guid orderId) => "/app/loja/pedidos/" + orderId;

    private DateOnly Today => BusinessTime.Today(clock);

    /// <summary>Pedido novo (CA 1 a 5): nasce <c>Draft</c>, preço da tabela padrão, total no servidor; para aplicativo, os limites (CA 13) e o id externo (CA 2).</summary>
    public async Task<Result<SalesOrderDetail>> CreateAsync(SalesOrderInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var external = string.IsNullOrWhiteSpace(input.ExternalId) ? null : input.ExternalId.Trim();
        if (external is { Length: > 80 })
        {
            return Field("externalId", "O id externo tem até 80 caracteres.");
        }

        var hash = external is null ? null : Hash(input with { ExternalId = null });
        if (external is not null && await ReplayExternalAsync(actor, external, hash!, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (Clean(input.Note) is { Length: > 500 })
        {
            return Field("note", "A observação tem até 500 caracteres.");
        }

        if (await CustomerErrorAsync(input.CustomerId, cancellationToken) is { } customerError)
        {
            return customerError;
        }

        var settings = await parameters.CurrentAsync(cancellationToken);
        if (actor.IsApp && await AppLimitsErrorAsync(input.CustomerId, input.Lines, actor, settings, cancellationToken) is { } limitError)
        {
            return limitError;
        }

        var location = await LocationAsync(input.StockLocationId, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error!;
        }

        var id = Guid.CreateVersion7();
        var built = await BuildLinesAsync(id, input.Lines, input.OrderDiscount, [], cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var now = clock.GetUtcNow();
        var order = SalesOrder.Create(id, await numbers.NextAsync(SalesOrderRules.DocType, cancellationToken), actor.Origin, actor.SourceAppId, Cut(actor.AppName, 80), external, hash,
            actor.UserId, input.CustomerId, input.StoreId, location.Value, Clean(input.Note), now, actor.ConnectionId);
        order.ApplyTotals(built.Value, input.OrderDiscount);
        // CA 5: acima do limite do aplicativo, só rascunho com "Desconto para aprovar".
        order.DiscountNeedsApproval = actor.IsApp && AboveLimit(order, built.Value, settings.AppDiscountLimitPct);
        db.SalesOrders.Add(order);
        db.SalesOrderLines.AddRange(built.Value);
        Record(order, null, "create", actor, null, now);
        events.Publish(new OrderCreated(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ExternalIndex))
        {
            // Duas entregas do mesmo pedido externo ao mesmo tempo: a outra gravou primeiro.
            Forget(order, built.Value);
            return await ReplayExternalAsync(actor, external!, hash!, cancellationToken) is { } again ? again : Error.Conflict("external_id_conflict", "Este id externo já é de outro pedido.");
        }

        return await DetailAsync(order.Id, cancellationToken) is { } detail ? detail : NotFound();
    }

    /// <summary>Edição (CA 8): rascunho ou aberto; no aberto, a reserva acompanha (sobe tudo ou nada, desce liberando). Depois da cobrança, travado.</summary>
    public async Task<Result<SalesOrderDetail>> UpdateAsync(Guid id, SalesOrderEditInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!SalesOrderRules.Can(order.Status, SalesOrderAction.Edit))
        {
            return order.Status == SalesOrderStatus.AwaitingPayment ? Error.Conflict(TransitionCode, LockedMessage) : InvalidTransition(order, SalesOrderAction.Edit);
        }

        if (Clean(input.Note) is { Length: > 500 })
        {
            return Field("note", "A observação tem até 500 caracteres.");
        }

        if (input.CustomerId != order.CustomerId && await CustomerErrorAsync(input.CustomerId, cancellationToken) is { } customerError)
        {
            return customerError;
        }

        var settings = await parameters.CurrentAsync(cancellationToken);
        if (actor.IsApp && AppQuantityError(input.Lines, settings) is { } quantityError)
        {
            return quantityError;
        }

        var location = await LocationAsync(input.StockLocationId ?? order.StockLocationId, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error!;
        }

        var current = await db.SalesOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        // No aberto, o preço já foi congelado na confirmação: as linhas do mesmo produto mantêm o preço; produto novo entra pelo preço da tabela.
        var built = await BuildLinesAsync(order.Id, input.Lines, input.OrderDiscount, order.Status == SalesOrderStatus.Open ? current : [], cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            db.SalesOrderLines.RemoveRange(current);
            db.SalesOrderLines.AddRange(built.Value);
            order.CustomerId = input.CustomerId;
            order.StockLocationId = location.Value;
            order.Note = Clean(input.Note);
            order.ApplyTotals(built.Value, input.OrderDiscount);
            order.UpdatedAt = now;
            if (actor.IsApp)
            {
                order.DiscountNeedsApproval = AboveLimit(order, built.Value, settings.AppDiscountLimitPct);
            }

            if (order.Status == SalesOrderStatus.Open)
            {
                var reserved = await ReserveAsync(order, built.Value, cancellationToken);
                if (reserved is not null)
                {
                    return reserved;
                }
            }

            Record(order, order.Status, "edit", actor, null, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(order.Id);
        }, cancellationToken);
    }

    /// <summary>
    /// "Confirmar pedido" (CA 7, 9 e 10): relê o preço (mudou: 409 <c>price_changed</c> até aceitar), confere o desconto (acima do limite só com
    /// <c>sales.approve</c>; aplicativo com desconto para aprovar: 403), reserva tudo ou nada na mesma transação e reserva o id da venda.
    /// </summary>
    public async Task<Result<SalesOrderDetail>> ConfirmAsync(Guid id, SalesOrderConfirmInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!SalesOrderRules.Can(order.Status, SalesOrderAction.Confirm))
        {
            return InvalidTransition(order, SalesOrderAction.Confirm);
        }

        var lines = await db.SalesOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        var settings = await parameters.CurrentAsync(cancellationToken);
        if (actor.IsApp)
        {
            if (order.DiscountNeedsApproval || AboveLimit(order, lines, settings.AppDiscountLimitPct))
            {
                return Error.Forbidden(ApprovalCode, "Desconto acima do limite do aplicativo: confirme o pedido no ERP com quem aprova em Vendas.");
            }

            // CA 13: confirmar pelo aplicativo só com cliente cobrável.
            if (!(await charges.ReadinessAsync(order.CustomerId, cancellationToken)).CustomerBillable)
            {
                return Error.Validation("customer_not_billable", "Complete o CPF/CNPJ e o endereço de cobrança do cliente antes de confirmar.");
            }
        }
        else if ((order.DiscountNeedsApproval || AboveLimit(order, lines, settings.DiscountLimitPct)) && !await permissions.HasPermissionAsync(SaleService.Module, "approve", cancellationToken))
        {
            return Error.Forbidden(ApprovalCode, ApprovalMessage);
        }

        // Preço congelado na confirmação (ECM-02 CA 5): relido agora; mudou, a tela mostra o novo e pede confirmação.
        var current = (await products.FindManyAsync(lines.Select(l => l.ProductId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.ProductId);
        var changed = lines.Where(l => current.TryGetValue(l.ProductId, out var p) && p.Price is { } price && SaleRules.Money(price) != l.UnitPrice).ToList();
        if (changed.Count > 0 && !input.AcceptNewPrices)
        {
            return Error.Conflict("price_changed", string.Join(" ", changed.Select(l =>
                $"O preço de {l.Description} mudou de {Money(l.UnitPrice)} para {Money(current[l.ProductId].Price!.Value)}.")) + " Confirme com o preço novo.");
        }

        foreach (var line in changed)
        {
            var product = current[line.ProductId];
            if (line.Discount > SaleRules.Money(line.Quantity * product.Price!.Value))
            {
                return Field("lines", $"O desconto de {line.Description} passa do valor da linha com o preço novo. Ajuste o desconto.");
            }

            line.Reprice(product.Price!.Value, product.PriceListVersion);
        }

        if (changed.Count > 0)
        {
            order.ApplyTotals(lines, order.OrderDiscount);
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var hold = now.AddHours(order.FromApp ? settings.AppHoldHours : settings.HoldHours);
            order.Confirm(Guid.CreateVersion7(), hold, now);
            order.DiscountNeedsApproval = false;
            var reserved = await ReserveAsync(order, lines, cancellationToken);
            if (reserved is not null)
            {
                return reserved;
            }

            Record(order, SalesOrderStatus.Draft, "confirm", actor, null, now);
            events.Publish(new OrderConfirmed(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now));
            await db.SaveChangesAsync(cancellationToken);
            if (order.FromApp)
            {
                await AlertUnpaidAppOrdersAsync(order, settings, cancellationToken);
            }

            return Result.Success(order.Id);
        }, cancellationToken);
    }

    public async Task<SalesOrderDetail?> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var lines = await db.SalesOrderLines.AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        var held = (await reservations.ForSourceAsync(SalesOrderRules.SourceType, id, cancellationToken)).Where(r => r.Status == ReservationState.Active)
            .GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        var charge = order.ReceivableId is null && order.Status != SalesOrderStatus.Cancelled ? null : await charges.FindAsync(id, cancellationToken);
        var party = await parties.FindAsync(order.CustomerId, cancellationToken);
        var location = (await locations.FindAsync([order.StockLocationId], cancellationToken)) is [var first, ..] ? first : null;
        var transitions = await db.SalesOrderTransitions.AsNoTracking().Where(t => t.OrderId == id).OrderBy(t => t.At).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string>();
        foreach (var actorId in transitions.Select(t => t.ActorId).OfType<Guid>().Distinct())
        {
            names[actorId] = await users.DisplayNameAsync(actorId, cancellationToken) ?? "Usuário";
        }

        // Reservado por linha: o do item, distribuído pela ordem das linhas.
        var left = new Dictionary<Guid, decimal>(held);
        var lineViews = lines.Select(l =>
        {
            var reserved = Math.Min(l.Quantity, left.GetValueOrDefault(l.ItemId));
            left[l.ItemId] = left.GetValueOrDefault(l.ItemId) - reserved;
            return new SalesOrderLineView(l.Id, l.LineNumber, l.ProductId, l.ItemId, l.Description, l.UnitCode, l.Quantity, l.UnitPrice, l.Discount, l.Total, reserved, l.PriceListVersion);
        }).ToList();
        var problems = Problems(order, lineViews, charge);
        return new SalesOrderDetail(order.Id, order.Number, order.Status, order.Origin, order.SourceAppName, order.ExternalId,
            order.FromApp ? SalesOrderRules.AppLabel(order.Origin, order.SourceAppName) : null, order.CustomerId, PartyName(party), order.StockLocationId, location?.Name, order.SaleId,
            order.ReceivableId, order.Subtotal, order.OrderDiscount, order.DiscountTotal, order.Total, order.HoldUntil, order.Note, order.CancelReason, order.CreatedAt, order.UpdatedAt,
            NextStep(order.Status), problems, lineViews, charge,
            transitions.Select(t => new SalesOrderTimelineEntry(t.At, t.FromStatus, t.ToStatus, t.Action, ActorName(t, names), t.Reason)).ToList(), order.Version);
    }

    /// <summary>Lista agrupada pelo próximo passo (com a contagem de cada grupo no filtro), mais recente primeiro; até 200 pedidos.</summary>
    public async Task<SalesOrderListPage> ListAsync(SalesOrderListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var orders = db.SalesOrders.AsNoTracking();
        if (!query.IncludeCancelled && query.Step is null)
        {
            orders = orders.Where(o => o.Status != SalesOrderStatus.Cancelled);
        }

        if (query.Origin is { } origin)
        {
            orders = orders.Where(o => o.Origin == origin);
        }

        if (query.SourceAppId is { } app)
        {
            orders = orders.Where(o => o.SourceAppId == app);
        }

        if (query.CustomerId is { } customer)
        {
            orders = orders.Where(o => o.CustomerId == customer);
        }

        if (query.From is { } from)
        {
            var start = BusinessTime.StartOfDayUtc(from);
            orders = orders.Where(o => o.CreatedAt >= start);
        }

        if (query.To is { } to)
        {
            var end = BusinessTime.StartOfDayUtc(to.AddDays(1));
            orders = orders.Where(o => o.CreatedAt < end);
        }

        if (query.StaleDays is > 0 and var days)
        {
            var limit = clock.GetUtcNow().AddDays(-days);
            orders = orders.Where(o => o.UpdatedAt < limit);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = "%" + query.Q.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal) + "%";
            orders = orders.Where(o => EF.Functions.ILike(o.Number, q) || (o.ExternalId != null && EF.Functions.ILike(o.ExternalId, q)));
        }

        var counts = await orders.GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var stepCounts = counts.GroupBy(c => NextStep(c.Status)).Where(g => g.Key != SalesOrderNextStep.None)
            .Select(g => new SalesOrderStepCount(g.Key, g.Sum(c => c.Count))).OrderBy(c => c.Step).ToList();
        if (query.Step is { } step)
        {
            var statuses = StatusesOf(step);
            orders = orders.Where(o => statuses.Contains(o.Status));
        }

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderByDescending(o => o.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(rows.Select(r => r.CustomerId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, PartyName);
        var now = clock.GetUtcNow();
        var items = rows.Select(o => new SalesOrderListItem(o.Id, o.Number, o.Status, o.Origin, o.SourceAppName, o.CustomerId, names.GetValueOrDefault(o.CustomerId, "Cliente"), o.Total,
            o.CreatedAt, o.UpdatedAt, o.HoldUntil, NextStep(o.Status), ListProblems(o), o.HoldUntil is { } hold && hold < now.AddHours(6))).ToList();
        return new SalesOrderListPage(items, total, stepCounts);
    }

    /// <summary>Card "Pedidos aguardando ação" do Início.</summary>
    public async Task<SalesOrderSummaryView> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var counts = await db.SalesOrders.AsNoTracking().Where(o => o.Status != SalesOrderStatus.Cancelled || o.PaidAfterCancel)
            .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var problems = await db.SalesOrders.AsNoTracking().CountAsync(o => o.PaidAfterCancel || (o.DiscountNeedsApproval && o.Status == SalesOrderStatus.Draft), cancellationToken);
        return new SalesOrderSummaryView(counts.GetValueOrDefault(SalesOrderStatus.Draft), counts.GetValueOrDefault(SalesOrderStatus.Open),
            counts.GetValueOrDefault(SalesOrderStatus.AwaitingPayment), problems);
    }

    /// <summary>Consulta rápida de produto (GTIN ou nome) com o preço e o disponível do local padrão.</summary>
    public async Task<IReadOnlyList<SalesOrderProductOption>> ProductOptionsAsync(string? q, Guid? locationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return [];
        }

        var term = q.Trim();
        var hits = new List<CatalogLookupHit>();
        if (term.All(char.IsAsciiDigit) && term.Length >= 8 && await lookup.ByGtinAsync(term, null, cancellationToken) is { } byGtin)
        {
            hits.Add(byGtin);
        }
        else
        {
            hits.AddRange(await lookup.SearchAsync(term, 20, null, cancellationToken));
        }

        var location = locationId ?? (await locations.DefaultAsync(cancellationToken))?.Id;
        var available = (await balances.GetAsync(hits.Select(h => h.Product.ItemId).Distinct().ToList(), location, cancellationToken))
            .GroupBy(a => a.ItemId).ToDictionary(g => g.Key, g => g.Sum(a => a.Available));
        return hits.Select(h => new SalesOrderProductOption(h.Product.ProductId, h.Product.Description, h.Product.UnitCode, h.Product.Price,
            available.GetValueOrDefault(h.Product.ItemId), h.Gtin)).ToList();
    }

    /// <summary>Clientes ativos (papel Cliente) para o pedido; o documento sai mascarado.</summary>
    public async Task<IReadOnlyList<SalesOrderCustomerOption>> CustomerOptionsAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Customer, 20, cancellationToken)).Select(p => new SalesOrderCustomerOption(p.Id, PartyName(p), Mask(p.Document))).ToList();

    // ---------- apoio ----------

    private async Task<SalesOrderDetail?> ReplayExternalAsync(SalesOrderActor actor, string external, string hash, CancellationToken ct)
    {
        var existing = await db.SalesOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Origin == actor.Origin && o.SourceAppId == actor.SourceAppId && o.ExternalId == external, ct);
        if (existing is null)
        {
            return null;
        }

        return existing.ExternalHash == hash && await DetailAsync(existing.Id, ct) is { } detail ? detail with { Replayed = true } : null;
    }

    /// <summary>Lê o pedido para uma ação: 404 se não existe (inclusive de outro tenant, IDOR); 409 "Recarregar" se a versão lida é outra.</summary>
    private async Task<Result<SalesOrder>> LoadAsync(Guid id, uint version, SalesOrderActor actor, CancellationToken ct)
    {
        var order = await db.SalesOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || (actor.IsApp && order.SourceAppId != actor.SourceAppId))
        {
            return NotFound();
        }

        return order.Version != version ? Stale() : order;
    }

    /// <summary>Roda a ação numa transação (ou na de quem chama); conflito de <c>xmin</c> vira 409 "Recarregar" e nada muda.</summary>
    private async Task<Result<SalesOrderDetail>> InTransactionAsync(Func<Task<Result<Guid>>> work, CancellationToken ct)
    {
        Result<Guid> result;
        if (db.Database.CurrentTransaction is not null)
        {
            result = await work();
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                result = await work();
                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(ct);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return Stale();
            }
        }

        if (result.IsFailure)
        {
            return result.Error!;
        }

        return await DetailAsync(result.Value, ct) is { } detail ? detail : NotFound();
    }

    /// <summary>Reserva as linhas no local do pedido (CA 9): faltando saldo, 409 <c>insufficient_stock</c> por linha e nada é reservado.</summary>
    private async Task<Error?> ReserveAsync(SalesOrder order, IReadOnlyList<SalesOrderLine> lines, CancellationToken ct)
    {
        var request = new SourceReservationRequest(SalesOrderRules.SourceType, order.Id, order.StockLocationId,
            lines.GroupBy(l => l.ItemId).Select(g => new SourceReservationLine(g.Key, g.Sum(l => l.Quantity))).ToList(), order.HoldUntil, "Pedido " + order.Number);
        var outcome = await reservations.ReserveAsync(request, ct);
        if (outcome.Reserved)
        {
            return null;
        }

        var messages = outcome.Shortages.Select(s =>
        {
            var line = lines.First(l => l.ItemId == s.ItemId);
            return $"Só há {Quantity(s.Available)} {line.UnitCode} disponíveis de {line.Description}.";
        }).ToArray();
        return new Error("insufficient_stock", string.Join(" ", messages), ErrorType.Conflict) { Fields = new Dictionary<string, string[]> { ["lines"] = messages } };
    }

    private async Task<Result<List<SalesOrderLine>>> BuildLinesAsync(Guid orderId, IReadOnlyList<SalesOrderLineInput> inputs, decimal orderDiscount, IReadOnlyList<SalesOrderLine> keepPrice,
        CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0 || inputs.Count > SalesOrderRules.MaxLines)
        {
            return Field("lines", $"O pedido precisa de 1 a {SalesOrderRules.MaxLines} linhas.");
        }

        if (inputs.Any(l => l.Quantity <= 0 || l.Quantity > 999_999 || l.Discount < 0))
        {
            return Field("lines", "Cada linha precisa de quantidade maior que zero e desconto não negativo.");
        }

        var byId = (await products.FindManyAsync(inputs.Select(l => l.ProductId).Distinct().ToList(), ct)).ToDictionary(p => p.ProductId);
        if (inputs.FirstOrDefault(l => !byId.ContainsKey(l.ProductId)) is { } unknown)
        {
            return Field("lines", "Produto não encontrado: " + unknown.ProductId + ".");
        }

        if (inputs.FirstOrDefault(l => !byId[l.ProductId].IsActive) is { } inactive)
        {
            return Field("lines", "Produto inativo: " + byId[inactive.ProductId].Description + ".");
        }

        var kept = keepPrice.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.First());
        var lines = new List<SalesOrderLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var product = byId[input.ProductId];
            decimal price;
            long version;
            if (kept.TryGetValue(input.ProductId, out var old))
            {
                (price, version) = (old.UnitPrice, old.PriceListVersion);
            }
            else if (product.Price is { } listed)
            {
                (price, version) = (listed, product.PriceListVersion);
            }
            else
            {
                return Field("lines", $"O produto {product.Description} está sem preço na tabela padrão.");
            }

            var line = SalesOrderLine.Create(orderId, i + 1, product.ProductId, product.ItemId, Cut(product.Description, 200)!, Cut(product.UnitCode, 10)!, input.Quantity, price,
                input.Discount, version);
            if (line.Discount > line.Gross)
            {
                return Field("lines", $"O desconto de {product.Description} passa do valor da linha.");
            }

            lines.Add(line);
        }

        var net = lines.Sum(l => l.Total);
        if (orderDiscount < 0 || SaleRules.Money(orderDiscount) > net)
        {
            return Field("orderDiscount", "O desconto do pedido não pode ser negativo nem passar do total das linhas.");
        }

        return lines;
    }

    private async Task<Error?> CustomerErrorAsync(Guid customerId, CancellationToken ct)
    {
        var party = customerId == Guid.Empty ? null : await parties.FindAsync(customerId, ct);
        if (party is null || party.IsDeleted || !party.IsActive || !party.Roles.Contains(PartyRoleKind.Customer))
        {
            return Field("customerId", "Escolha um cliente ativo (terceiro com o papel Cliente).");
        }

        return null;
    }

    private async Task<Result<Guid>> LocationAsync(Guid? requested, CancellationToken ct)
    {
        if (requested is { } id)
        {
            var found = (await locations.FindAsync([id], ct)) is [var first, ..] ? first : null;
            return found is { IsActive: true } ? id : Field("stockLocationId", "Local de estoque não encontrado ou inativo.");
        }

        return (await locations.DefaultAsync(ct))?.Id is { } fallback ? fallback : Field("stockLocationId", "Cadastre o local padrão do estoque ou informe o local do pedido.");
    }

    /// <summary>CA 13: quantidade por linha e por pedido e no máximo 5 pedidos em aberto por cliente e aplicativo.</summary>
    private async Task<Error?> AppLimitsErrorAsync(Guid customerId, IReadOnlyList<SalesOrderLineInput> lines, SalesOrderActor actor, SalesOrderParameters settings, CancellationToken ct)
    {
        if (AppQuantityError(lines, settings) is { } quantity)
        {
            return quantity;
        }

        var open = await db.SalesOrders.CountAsync(o => o.CustomerId == customerId && o.SourceAppId == actor.SourceAppId
            && (o.Status == SalesOrderStatus.Draft || o.Status == SalesOrderStatus.Open || o.Status == SalesOrderStatus.AwaitingPayment), ct);
        return open >= SalesOrderRules.MaxOpenOrdersPerCustomerApp
            ? Error.Conflict("too_many_open_orders", $"Este cliente já tem {SalesOrderRules.MaxOpenOrdersPerCustomerApp} pedidos em aberto por este aplicativo.")
            : null;
    }

    private static Error? AppQuantityError(IReadOnlyList<SalesOrderLineInput> lines, SalesOrderParameters settings) =>
        lines.Any(l => l.Quantity > settings.AppMaxLineQty) || lines.Sum(l => l.Quantity) > settings.AppMaxOrderQty
            ? Error.Validation("app_quantity_limit", $"Pedido do aplicativo: até {Quantity(settings.AppMaxLineQty)} por linha e {Quantity(settings.AppMaxOrderQty)} no pedido.")
            : null;

    /// <summary>CA 13: acima do limite de pedidos do aplicativo com reserva e sem pagamento, os Proprietários recebem o alerta (uma vez por dia).</summary>
    private async Task AlertUnpaidAppOrdersAsync(SalesOrder order, SalesOrderParameters settings, CancellationToken ct)
    {
        var unpaid = await db.SalesOrders.CountAsync(o => o.SourceAppId == order.SourceAppId && (o.Status == SalesOrderStatus.Open || o.Status == SalesOrderStatus.AwaitingPayment), ct);
        if (unpaid <= settings.AppUnpaidAlertCount)
        {
            return;
        }

        var link = "/app/loja/pedidos?app=" + order.SourceAppId;
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            await notifications.NotifyOnceAsync(owner, NotificationType,
                $"{unpaid} pedidos de {order.SourceAppName ?? "aplicativo"} estão com estoque reservado e sem pagamento.", link,
                "sales.app-unpaid:" + order.SourceAppId + ":" + Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct);
        }
    }

    private void Record(SalesOrder order, SalesOrderStatus? from, string action, SalesOrderActor actor, string? reason, DateTimeOffset at, string? label = null) =>
        db.SalesOrderTransitions.Add(new SalesOrderTransition
        {
            OrderId = order.Id, FromStatus = from, ToStatus = order.Status, Action = action, ActorId = actor.UserId, ActorLabel = label ?? actor.Label, Origin = actor.Origin,
            Reason = Cut(reason, 200), At = at,
        });

    private static bool AboveLimit(SalesOrder order, IReadOnlyCollection<SalesOrderLine> lines, decimal limitPct) =>
        SalesOrderRules.DiscountAboveLimit(lines.Select(l => (l.Gross, l.Discount)), order.OrderDiscount, order.Subtotal, limitPct);

    private static List<SalesOrderProblem> Problems(SalesOrder order, List<SalesOrderLineView> lines, OrderChargeView? charge)
    {
        var problems = ListProblems(order).ToList();
        if (order.Status is SalesOrderStatus.Open or SalesOrderStatus.AwaitingPayment && lines.Any(l => l.Reserved < l.Quantity))
        {
            problems.Add(SalesOrderProblem.ReservationShort);
        }

        if (order.Status == SalesOrderStatus.AwaitingPayment && charge?.Slip?.Status == OrderSlipStatus.PaidPendingReview)
        {
            problems.Add(SalesOrderProblem.PaymentInReview);
        }

        return problems;
    }

    private static List<SalesOrderProblem> ListProblems(SalesOrder order)
    {
        var problems = new List<SalesOrderProblem>();
        if (order.DiscountNeedsApproval && order.Status == SalesOrderStatus.Draft)
        {
            problems.Add(SalesOrderProblem.DiscountNeedsApproval);
        }

        if (order.PaidAfterCancel)
        {
            problems.Add(SalesOrderProblem.PaidAfterCancel);
        }

        return problems;
    }

    public static SalesOrderNextStep NextStep(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Draft => SalesOrderNextStep.Confirm,
        SalesOrderStatus.Open => SalesOrderNextStep.Charge,
        SalesOrderStatus.AwaitingPayment => SalesOrderNextStep.AwaitPayment,
        SalesOrderStatus.Paid => SalesOrderNextStep.Pick,
        _ => SalesOrderNextStep.None,
    };

    private static List<SalesOrderStatus> StatusesOf(SalesOrderNextStep step) => step switch
    {
        SalesOrderNextStep.Confirm => [SalesOrderStatus.Draft],
        SalesOrderNextStep.Charge => [SalesOrderStatus.Open],
        SalesOrderNextStep.AwaitPayment => [SalesOrderStatus.AwaitingPayment],
        SalesOrderNextStep.Pick => [SalesOrderStatus.Paid],
        _ => [SalesOrderStatus.Cancelled],
    };

    private static string ActorName(SalesOrderTransition t, Dictionary<Guid, string> names)
    {
        var person = t.ActorId is { } id ? names.GetValueOrDefault(id) : null;
        return t.ActorLabel is { } label ? (person is null ? label : $"{label} (autorizada por {person})") : person ?? "Sistema";
    }

    /// <summary>Hash do corpo do pedido externo (sem o id externo): o mesmo id com outro corpo é conflito.</summary>
    private static string Hash(SalesOrderInput input)
    {
        var normalized = new
        {
            input.CustomerId, Lines = input.Lines.Select(l => new { l.ProductId, Quantity = SaleRules.Quantity(l.Quantity), Discount = SaleRules.Money(l.Discount) }),
            OrderDiscount = SaleRules.Money(input.OrderDiscount), input.StockLocationId, input.StoreId, Note = Clean(input.Note),
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
    }

    private void Forget(SalesOrder order, IEnumerable<SalesOrderLine> lines)
    {
        foreach (var entry in db.SalesOrderTransitions.Local.Where(t => t.OrderId == order.Id).ToList())
        {
            db.SalesOrderTransitions.Entry(entry).State = EntityState.Detached;
        }

        foreach (var line in lines)
        {
            db.SalesOrderLines.Entry(line).State = EntityState.Detached;
        }

        db.SalesOrders.Entry(order).State = EntityState.Detached;
    }

    internal static string PartyName(PartySummary? party) => party is null ? "Cliente removido" : party.TradeName ?? party.LegalName;

    private static string? Mask(string? document) =>
        string.IsNullOrEmpty(document) || document.Length < 4 ? document : new string('•', document.Length - 4) + document[^4..];

    internal static Error InvalidTransition(SalesOrder order, SalesOrderAction action) => Error.Conflict(TransitionCode, SalesOrderRules.TransitionMessage(order.Status, action));

    internal static Error Stale() => Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O pedido mudou desde que você abriu. Recarregue.");

    internal static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Pedido não encontrado.");

    internal static string Money(decimal value) => value.ToString("C", PtBr);

    private static string Quantity(decimal value) => value.ToString("0.####", PtBr);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Cut(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
