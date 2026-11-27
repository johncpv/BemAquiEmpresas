using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Finance;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>
/// VND-02a, integração (Testcontainers + fake do Inter): pedido de venda do rascunho ao pago à mão e por boleto, reserva tudo ou nada e a disputa pela
/// última unidade, recebível sem duplicar, idempotência, pago depois de cancelado, corrida pagamento × cancelamento, cancelamento do pago com devolução,
/// expiração e as regras do aplicativo (pelo serviço, com o principal de aplicativo; a rota é da INT-03).
/// </summary>
public sealed class SalesOrderTests(BillingSlipFixture fixture) : IClassFixture<BillingSlipFixture>
{
    private const string Orders = "/api/v1/sales/orders";

    private sealed record Setup(HttpClient Owner, Guid OwnerId, Guid Customer, Guid Location, Guid Cash, Guid Slip);

    private Setup? _setup;

    /// <summary>Perfil Comércio (libera o <c>sales</c>), Proprietário, integração Inter, cliente cobrável, local próprio e as formas (dinheiro e boleto).</summary>
    private async Task<Setup> SetupAsync()
    {
        if (_setup is not null)
        {
            return _setup;
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>()
                .ApplyProfileAsync(fixture.TenantA.Id, BusinessProfileCatalog.Retail, null, null);
            result.IsSuccess.Should().BeTrue();
        }

        fixture.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        var (ownerId, owner) = await fixture.ActorAsync(owner: true);
        var (account, _) = await fixture.IntegrationAsync(owner);
        var customer = await CustomerAsync(owner);
        var location = await fixture.QueryAsync(async db =>
        {
            var l = new Location { Code = "PV-" + Guid.NewGuid().ToString("N")[..5], Name = "Loja pedidos", CreatedAt = DateTimeOffset.UtcNow };
            db.Locations.Add(l);
            await db.SaveChangesAsync();
            return l.Id;
        });
        var cashAccount = await Created(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa PV " + Guid.NewGuid().ToString("N")[..4], kind = "Cash" }));
        var cash = await Created(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = "Dinheiro PV", type = "Cash", destinationAccountId = cashAccount, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }));
        var slip = await Created(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = "Boleto com Pix", type = "BankSlip", destinationAccountId = account, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }));
        var saved = await owner.PutAsJsonAsync(Orders + "/parameters", new
        {
            discountLimitPct = 10m, appDiscountLimitPct = 0m, holdHours = 48, appHoldHours = 2, chargeDays = 1, appChargeDays = 3, appMaxLineQty = 50m, appMaxOrderQty = 200m,
            appUnpaidAlertCount = 10, bankSlipPaymentMethodId = slip,
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        _setup = new Setup(owner, ownerId, customer, location, cash, slip);
        return _setup;
    }

    private static async Task<Guid> Created(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Produto do Catálogo com preço de <paramref name="price"/> na tabela padrão e <paramref name="stock"/> no local; devolve (produto, item).</summary>
    private async Task<(Guid Product, Guid Item)> ProductAsync(Guid location, decimal stock, decimal price = 100m)
    {
        await using var scope = await fixture.ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.Finished, Code = "PVI-" + Guid.NewGuid().ToString("N")[..6], Name = "Peça " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        var defaults = scope.ServiceProvider.GetRequiredService<BemAqui.Catalog.Application.CatalogDefaults>();
        var product = new BemAqui.Catalog.Domain.Product { ItemId = item.Id, CategoryId = (await defaults.GeneralCategoryAsync()).Id, SaleName = item.Name, CreatedAt = DateTimeOffset.UtcNow };
        db.Products.Add(product);
        db.PriceListItems.Add(new BemAqui.Catalog.Domain.PriceListItem { PriceListId = (await defaults.DefaultPriceListAsync()).Id, ProductId = product.Id, Price = price, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        if (stock > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>()
                .PostAsync(new StockPosting(StockMovementType.Receipt, item.Id, location, null, stock, 40m, StockSources.GoodsReceipt, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        return (product.Id, item.Id);
    }

    private static HttpRequestMessage Post(string url, object body, Guid? key = null, bool withKey = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (withKey)
        {
            request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        }

        return request;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static uint Version(JsonElement order) => order.GetProperty("version").GetUInt32();

    private static Guid Id(JsonElement order) => order.GetProperty("id").GetGuid();

    private static async Task<JsonElement> DraftAsync(Setup s, Guid product, decimal quantity, decimal discount = 0m, HttpClient? client = null) =>
        await Ok(await (client ?? s.Owner).SendAsync(Post(Orders, new { customerId = s.Customer, stockLocationId = s.Location, lines = new[] { new { productId = product, quantity, discount, unitPrice = 1m } } })),
            HttpStatusCode.Created);

    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, JsonElement order) =>
        await client.PostAsJsonAsync($"{Orders}/{Id(order)}/confirm", new { version = Version(order), acceptNewPrices = false });

    private Task<decimal> ReservedAsync(Guid item, Guid location) =>
        fixture.QueryAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => p.QuantityReserved).FirstOrDefaultAsync());

    private Task<decimal> OnHandAsync(Guid item, Guid location) =>
        fixture.QueryAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => p.QuantityOnHand).FirstOrDefaultAsync());

    private Task<BemAqui.Sales.Domain.SalesOrder> OrderAsync(Guid id) => fixture.QueryAsync(db => db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == id));

    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente; nenhum consumidor pode falhar.</summary>
    private async Task DrainAsync()
    {
        var info = (await fixture.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(fixture.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await fixture.ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await fixture.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                var errors = await fixture.QueryAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.Error != null).Select(m => m.Type + ": " + m.Error).ToListAsync());
                errors.Should().BeEmpty("nenhum consumidor pode falhar");
                return;
            }

            await Task.Delay(250);
        }
    }

    private async Task ReconcileAsync()
    {
        await using var scope = await fixture.ScopeAsync();
        await scope.ServiceProvider.GetRequiredService<InterBillingReconciler>().RunAsync();
    }

    /// <summary>Pedido aberto (confirmado) e cobrado, com o boleto já emitido; devolve o pedido e a cobrança simulada no Inter.</summary>
    private async Task<(JsonElement Order, FakeCharge Charge)> ChargedAsync(Setup s, Guid product, decimal quantity)
    {
        var order = await Ok(await ConfirmAsync(s.Owner, await DraftAsync(s, product, quantity)));
        var draft = await Ok(await s.Owner.GetAsync($"{Orders}/{Id(order)}/charge"));
        draft.GetProperty("customerBillable").GetBoolean().Should().BeTrue();
        draft.GetProperty("paymentMethodConfigured").GetBoolean().Should().BeTrue();
        var charged = await Ok(await s.Owner.SendAsync(Post($"{Orders}/{Id(order)}/charge", new { dueDate = draft.GetProperty("dueDate").GetString(), sendEmail = false, version = Version(order) })));
        charged.GetProperty("status").GetString().Should().Be("AwaitingPayment");
        var slipId = charged.GetProperty("charge").GetProperty("slip").GetProperty("id").GetGuid();
        await fixture.IssueJobAsync(slipId);
        var slip = await fixture.QueryAsync(db => db.BillingSlips.AsNoTracking().SingleAsync(x => x.Id == slipId));
        return (charged, fixture.Inter.Billing.ByReference(slip.OurReference)!);
    }

    [Fact]
    public async Task Rascunho_confirmado_reserva_e_pago_a_mao_vira_venda_que_consome_a_reserva()
    {
        var s = await SetupAsync();
        var (product, item) = await ProductAsync(s.Location, 10);

        // CA 3: sem Idempotency-Key, recusa.
        var noKey = await s.Owner.SendAsync(Post(Orders, new { customerId = s.Customer, lines = new[] { new { productId = product, quantity = 1m, discount = 0m } } }, withKey: false));
        (await Ok(noKey, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("idempotency_key_required");

        // CA 1 e 5: nasce rascunho, número PV, preço da tabela (o unitPrice do corpo é ignorado) e o total do servidor.
        var draft = await DraftAsync(s, product, 3, discount: 10m);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("number").GetString().Should().MatchRegex(@"^PV-\d{4}-\d{4}$");
        draft.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().Should().Be(100m);
        draft.GetProperty("total").GetDecimal().Should().Be(290m);
        draft.GetProperty("nextStep").GetString().Should().Be("Confirm");
        (await ReservedAsync(item, s.Location)).Should().Be(0m, "rascunho não reserva");

        // CA 9: confirmar reserva; o disponível desce.
        var open = await Ok(await ConfirmAsync(s.Owner, draft));
        open.GetProperty("status").GetString().Should().Be("Open");
        open.GetProperty("lines")[0].GetProperty("reserved").GetDecimal().Should().Be(3m);
        (await ReservedAsync(item, s.Location)).Should().Be(3m);

        // CA 6: transição fora da máquina.
        var again = await ConfirmAsync(s.Owner, open);
        var problem = await Ok(again, HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().Should().Be(SalesOrderService.TransitionCode);
        problem.GetProperty("detail").GetString().Should().Be("Este pedido está aberto e não pode ser confirmado.");

        // CA 20: registrar pagamento à mão (dinheiro, com troco) vira venda pelo IOrderPayments.
        var paid = await Ok(await s.Owner.SendAsync(Post($"{Orders}/{Id(open)}/payments", new { paymentMethodId = s.Cash, version = Version(open), amountReceived = 300m })));
        paid.GetProperty("status").GetString().Should().Be("Paid");
        var saleId = paid.GetProperty("saleId").GetGuid();
        var sale = await fixture.QueryAsync(db => db.Sales.AsNoTracking().SingleAsync(x => x.Id == saleId));
        sale.SourceType.Should().Be("SalesOrder");
        sale.Channel.Should().Be(SaleChannel.Online);
        sale.Total.Should().Be(290m);

        // CA 22: a baixa da venda consome a reserva (o reservado desce com o físico).
        await DrainAsync();
        (await OnHandAsync(item, s.Location)).Should().Be(7m);
        (await ReservedAsync(item, s.Location)).Should().Be(0m);
        (await fixture.QueryAsync(db => db.Reservations.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.SourceId == Id(open)))).Status.Should().Be(ReservationState.Consumed);
        (await fixture.QueryAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("Order")).Select(m => m.Payload).ToListAsync()))
            .Count(p => p.Contains(Id(open).ToString(), StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(3, "OrderCreated, OrderConfirmed e OrderPaid no outbox");

        // CA 28: id inexistente responde 404.
        (await s.Owner.GetAsync($"{Orders}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reserva_tudo_ou_nada_e_so_um_pedido_leva_a_ultima_unidade()
    {
        var s = await SetupAsync();
        var (product, item) = await ProductAsync(s.Location, 3);

        // CA 11: duas reservas de pedidos diferentes no mesmo item e local convivem.
        await Ok(await ConfirmAsync(s.Owner, await DraftAsync(s, product, 1)));
        await Ok(await ConfirmAsync(s.Owner, await DraftAsync(s, product, 1)));
        (await ReservedAsync(item, s.Location)).Should().Be(2m);

        // CA 10: dois pedidos disputando a última unidade ao mesmo tempo: só um reserva.
        var a = await DraftAsync(s, product, 1);
        var b = await DraftAsync(s, product, 1);
        var results = await Task.WhenAll(ConfirmAsync(s.Owner, a), ConfirmAsync(s.Owner, b));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var refused = await Ok(results.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict);
        refused.GetProperty("code").GetString().Should().Be("insufficient_stock");
        refused.GetProperty("detail").GetString().Should().StartWith("Só há 0 UN disponíveis de");
        (await ReservedAsync(item, s.Location)).Should().Be(3m);

        // CA 9: tudo ou nada; nada reservado na recusa.
        var big = await DraftAsync(s, product, 5);
        (await ConfirmAsync(s.Owner, big)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OrderAsync(Id(big))).Status.Should().Be(SalesOrderStatus.Draft);
        (await ReservedAsync(item, s.Location)).Should().Be(3m);
    }

    [Fact]
    public async Task Boleto_pago_vira_venda_uma_vez_sem_outro_recebivel_e_o_cancelamento_devolve_o_valor()
    {
        var s = await SetupAsync();
        var (product, item) = await ProductAsync(s.Location, 5);
        var (order, charge) = await ChargedAsync(s, product, 2);
        var orderId = Id(order);

        charge.Pay(200m, Today);
        await ReconcileAsync();
        await DrainAsync();

        // CA 18 e 22: pago pela baixa do título, vira a venda com o pagamento ligado ao título.
        var paid = await OrderAsync(orderId);
        paid.Status.Should().Be(SalesOrderStatus.Paid);
        var payment = await fixture.QueryAsync(db => db.SalePayments.AsNoTracking().SingleAsync(p => p.SaleId == paid.SaleId));
        payment.ReceivableId.Should().Be(paid.ReceivableId);
        (await OnHandAsync(item, s.Location)).Should().Be(3m);

        // CA 23: nenhum recebível de venda para a forma do boleto.
        var keys = await fixture.QueryAsync(db => db.Receivables.AsNoTracking().Where(r => r.SaleGroupKey != null).Select(r => r.SaleGroupKey!).ToListAsync());
        keys.Should().NotContain(k => k.Contains(s.Slip.ToString(), StringComparison.OrdinalIgnoreCase));

        // CA 24: a mesma baixa entregue de novo não faz outra venda.
        await using (var scope = await fixture.ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<SalesOrderService>().HandleSettledAsync(paid.ReceivableId!.Value);
        }

        (await fixture.QueryAsync(db => db.Sales.CountAsync(x => x.SourceId == orderId))).Should().Be(1);

        // CA 26: cancelar o pago exige a caixa marcada; com ela, estorna a venda e registra a devolução contra o título.
        var detail = await Ok(await s.Owner.GetAsync($"{Orders}/{orderId}"));
        var missing = await s.Owner.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Cliente desistiu", version = Version(detail), refundedOutside = false });
        (await Ok(missing, HttpStatusCode.UnprocessableEntity)).GetProperty("detail").GetString().Should().Contain("Já devolvi o valor ao cliente");
        var cancelled = await Ok(await s.Owner.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Cliente desistiu", version = Version(detail), refundedOutside = true }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        await DrainAsync();
        (await fixture.QueryAsync(db => db.Sales.AsNoTracking().SingleAsync(x => x.Id == paid.SaleId))).Status.Should().Be(SaleStatus.Cancelled);
        var receivable = await fixture.QueryAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == paid.ReceivableId));
        receivable.RefundedAt.Should().NotBeNull();
        receivable.Status.Should().NotBe(BemAqui.Finance.Contracts.ReceivableStatus.Open);
        (await fixture.QueryAsync(db => db.FinancialTransactions.Where(t => t.ReceivableId == receivable.Id).SumAsync(t => t.Amount))).Should().Be(0m, "entrada e devolução: líquido zero");
        (await OnHandAsync(item, s.Location)).Should().Be(5m, "o estorno da venda devolve o estoque");
    }

    [Fact]
    public async Task Boleto_pago_depois_de_cancelado_ganha_alerta_e_reabrir_reserva_de_novo()
    {
        var s = await SetupAsync();
        var (product, item) = await ProductAsync(s.Location, 4);
        var (order, charge) = await ChargedAsync(s, product, 1);
        var orderId = Id(order);

        var cancelled = await Ok(await s.Owner.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Cliente pediu", version = Version(order) }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        (await ReservedAsync(item, s.Location)).Should().Be(0m, "CA 25: a reserva sai");

        // CA 19: o banco pagou antes de confirmar o cancelamento: nada muda sozinho, só o alerta.
        charge.Pay(100m, Today);
        await ReconcileAsync();
        await DrainAsync();
        var after = await OrderAsync(orderId);
        after.Status.Should().Be(SalesOrderStatus.Cancelled);
        after.PaidAfterCancel.Should().BeTrue();
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Type == SalesOrderService.NotificationType))).Should().BeTrue();

        var detail = await Ok(await s.Owner.GetAsync($"{Orders}/{orderId}"));
        detail.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).Should().Contain("PaidAfterCancel");
        var reopened = await Ok(await s.Owner.PostAsJsonAsync($"{Orders}/{orderId}/reopen", new { version = Version(detail) }));
        reopened.GetProperty("status").GetString().Should().Be("Paid");
        await DrainAsync();
        (await OnHandAsync(item, s.Location)).Should().Be(3m);
        (await ReservedAsync(item, s.Location)).Should().Be(0m);
    }

    [Fact]
    public async Task Registrar_pagamento_reconsulta_o_boleto_e_recusa_quando_ja_foi_pago()
    {
        var s = await SetupAsync();
        var (product, _) = await ProductAsync(s.Location, 2);
        var (order, charge) = await ChargedAsync(s, product, 1);
        charge.Pay(100m, Today);

        var refused = await s.Owner.SendAsync(Post($"{Orders}/{Id(order)}/payments", new { paymentMethodId = s.Cash, version = Version(order) }));
        (await Ok(refused, HttpStatusCode.Conflict)).GetProperty("detail").GetString().Should().StartWith("Este boleto já foi pago em").And.EndWith("O pedido fica pago pela baixa do banco.");
        await DrainAsync();
        (await OrderAsync(Id(order))).Status.Should().Be(SalesOrderStatus.Paid, "a reconsulta aplicou a baixa do banco");
    }

    [Fact]
    public async Task Baixa_e_cancelamento_ao_mesmo_tempo_so_um_vence()
    {
        var s = await SetupAsync();
        var (product, _) = await ProductAsync(s.Location, 2);
        var (order, charge) = await ChargedAsync(s, product, 1);
        charge.Pay(100m, Today);
        await ReconcileAsync(); // título baixado; o ReceivableSettled ainda no outbox

        var cancel = s.Owner.PostAsJsonAsync($"{Orders}/{Id(order)}/cancel", new { reason = "Desistência", version = Version(order) });
        var drain = DrainAsync();
        await Task.WhenAll(cancel, drain);
        await DrainAsync();

        var final = await OrderAsync(Id(order));
        if ((await cancel).StatusCode == HttpStatusCode.OK)
        {
            final.Status.Should().Be(SalesOrderStatus.Cancelled);
            final.PaidAfterCancel.Should().BeTrue("cancelado primeiro, a baixa cai no pago depois de cancelado");
        }
        else
        {
            (await cancel).StatusCode.Should().Be(HttpStatusCode.Conflict);
            final.Status.Should().Be(SalesOrderStatus.Paid);
        }

        (await fixture.QueryAsync(db => db.Sales.CountAsync(x => x.SourceId == Id(order) && x.Status != SaleStatus.Cancelled))).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Prazo_vencido_cancela_libera_a_reserva_e_pago_nao_expira()
    {
        var s = await SetupAsync();
        var (product, item) = await ProductAsync(s.Location, 5);
        var open = await Ok(await ConfirmAsync(s.Owner, await DraftAsync(s, product, 2)));
        var paidOrder = await Ok(await ConfirmAsync(s.Owner, await DraftAsync(s, product, 1)));
        await Ok(await s.Owner.SendAsync(Post($"{Orders}/{Id(paidOrder)}/payments", new { paymentMethodId = s.Cash, version = Version(paidOrder) })));
        var past = DateTimeOffset.UtcNow.AddHours(-49);
        await fixture.QueryAsync(db => db.SalesOrders.Where(o => o.Id == Id(open) || o.Id == Id(paidOrder)).ExecuteUpdateAsync(u => u.SetProperty(o => o.HoldUntil, past)));

        await using (var scope = await fixture.ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<SalesOrderService>().ExpireAsync()).Should().BeGreaterThanOrEqualTo(1);
        }

        var expired = await OrderAsync(Id(open));
        expired.Status.Should().Be(SalesOrderStatus.Cancelled);
        expired.CancelReason.Should().Be("Prazo de pagamento vencido");
        (await OrderAsync(Id(paidOrder))).Status.Should().Be(SalesOrderStatus.Paid);
        await DrainAsync();
        (await ReservedAsync(item, s.Location)).Should().Be(0m);
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Link == "/app/loja/pedidos/novo?de=" + Id(open)))).Should().BeTrue();
    }

    [Fact]
    public async Task Regras_de_desconto_e_do_aplicativo()
    {
        var s = await SetupAsync();
        var (product, _) = await ProductAsync(s.Location, 20);

        // CA 7: desconto acima de 10% só confirma quem aprova em Vendas.
        var (_, seller) = await fixture.ActorAsync(owner: false, "sales.view", "sales.create", "sales.edit");
        var discounted = await DraftAsync(s, product, 1, discount: 20m, client: seller);
        var denied = await ConfirmAsync(seller, discounted);
        (await Ok(denied, HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be(SalesOrderService.ApprovalCode);
        await Ok(await ConfirmAsync(s.Owner, discounted));

        var app = new SalesOrderActor(SalesOrderOrigin.Conecta, s.OwnerId, Guid.NewGuid(), "Conecta");
        await using var scope = await fixture.ScopeAsync();
        var service = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        SalesOrderInput Input(decimal quantity, decimal discount = 0m, string? external = null, Guid? customer = null) =>
            new(customer ?? s.Customer, [new SalesOrderLineInput(product, quantity, discount, UnitPrice: 0.01m)], StockLocationId: s.Location, ExternalId: external);

        // CA 4 e 5: pedido do aplicativo nasce rascunho, sem reserva; preço do corpo ignorado; desconto acima de 0% vira "Desconto para aprovar".
        var created = await service.CreateAsync(Input(1, discount: 5m), app);
        created.IsSuccess.Should().BeTrue();
        created.Value.AppLabel.Should().Be("Pedido do Conecta: confirme com o cliente");
        created.Value.Lines[0].UnitPrice.Should().Be(100m);
        created.Value.Problems.Should().Contain(SalesOrderProblem.DiscountNeedsApproval);
        var confirm = await service.ConfirmAsync(created.Value.Id, new SalesOrderConfirmInput(created.Value.Version), app);
        confirm.Error!.Code.Should().Be(SalesOrderService.ApprovalCode);

        // CA 2: o mesmo id externo com o mesmo corpo devolve o pedido; com outro corpo, 409.
        var first = await service.CreateAsync(Input(2, external: "ML-1"), app);
        var replay = await service.CreateAsync(Input(2, external: "ML-1"), app);
        replay.Value.Id.Should().Be(first.Value.Id);
        replay.Value.Replayed.Should().BeTrue();
        (await service.CreateAsync(Input(3, external: "ML-1"), app)).Error!.Code.Should().Be("external_id_conflict");

        // CA 13: limites de quantidade e de pedidos em aberto por cliente e aplicativo.
        (await service.CreateAsync(Input(51), app)).Error!.Code.Should().Be("app_quantity_limit");
        for (var i = 0; i < 3; i++)
        {
            (await service.CreateAsync(Input(1), app)).IsSuccess.Should().BeTrue("até 5 pedidos em aberto");
        }

        (await service.CreateAsync(Input(1), app)).Error!.Code.Should().Be("too_many_open_orders");

        // CA 13: confirmar pelo aplicativo só com cliente cobrável.
        var noAddress = await BillingSlipFixture.CustomerAsync(s.Owner, address: false);
        var other = await service.CreateAsync(Input(1, customer: noAddress), app);
        (await service.ConfirmAsync(other.Value.Id, new SalesOrderConfirmInput(other.Value.Version), app)).Error!.Code.Should().Be("customer_not_billable");
    }
}
