using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Host.Persistence;
using BemAqui.Sales.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>VND-01 CA 3 a 8: ingestão na transação de quem chama, idempotência, soma dos pagamentos, validação por canal, pedido e cartão.</summary>
public partial class SalesTests
{
    [Fact]
    public async Task Ingestao_grava_na_transacao_de_quem_chama_desfaz_junto_e_poe_o_evento_no_outbox()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 10, 4m);
        var day = Today.AddDays(-11);

        // CA 3: dentro da transação de quem chama; o rollback dela desfaz venda, linhas, partes, pagamentos e o evento.
        var rolledBack = Online(day, location, [Line(product, 2, 15m)], [Paid(methods.Pix, 30m)]);
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<ISaleIngestion>().IngestAsync(rolledBack)).IsSuccess.Should().BeTrue();
            (await db.Sales.CountAsync(s => s.Id == rolledBack.SaleId)).Should().Be(1, "visível na transação de quem chamou");
            await transaction.RollbackAsync();
        }

        (await WithDbAsync(db => db.Sales.CountAsync(s => s.Id == rolledBack.SaleId))).Should().Be(0);
        (await WithDbAsync(db => db.SalePayments.CountAsync(p => p.SaleId == rolledBack.SaleId))).Should().Be(0);
        (await OutboxCountAsync(rolledBack.SaleId)).Should().Be(0);

        // CA 3 e 6: canal online sem caixa nem sessão; venda, linhas, parte única e pagamento gravados; SaleCompleted no outbox.
        var request = Online(day, location, [Line(product, 2, 15m, 2m), Line(product, 1, 12m)], [Paid(methods.Cash, 50m, change: 10m)]);
        var result = await IngestAsync(request);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Total.Should().Be(40m, "2 × 15 − 2 + 12");
        result.Value.Outcome.Should().Be(SaleIngestionOutcome.Accepted);
        var sale = await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == request.SaleId));
        sale.RegisterId.Should().BeNull();
        sale.CashSessionId.Should().BeNull();
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.BusinessDate.Should().Be(day);
        var item = ItemOf(product);
        (await WithDbAsync(db => db.SaleLines.CountAsync(l => l.SaleId == sale.Id && l.ItemId == item))).Should().Be(2);
        (await WithDbAsync(db => db.SaleParts.CountAsync(p => p.SaleId == sale.Id && p.Paid))).Should().Be(1);
        (await WithDbAsync(db => db.OutboxMessages.Where(m => m.Type == IntegrationEventRegistry.NameOf(typeof(SaleCompleted))).Select(m => m.Payload).ToListAsync()))
            .Should().Contain(p => p.Contains(sale.Id.ToString(), StringComparison.Ordinal) && p.Contains("\"Channel\":\"Online\"", StringComparison.Ordinal));
        await DrainAsync();
    }

    [Fact]
    public async Task Mesma_venda_entregue_duas_vezes_nao_duplica_e_devolve_a_primeira_resposta()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 2m);
        var request = Online(Today.AddDays(-12), location, [Line(product, 1, 9.9m)], [Paid(methods.Pix, 9.9m)]);

        var first = await IngestAsync(request);
        var second = await IngestAsync(request with { Lines = [Line(product, 3, 1m)] });
        first.IsSuccess.Should().BeTrue(first.Error?.Message);
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeEquivalentTo(first.Value with { Replayed = true }, "a resposta é a da primeira (CA 4)");
        (await WithDbAsync(db => db.Sales.CountAsync(s => s.Id == request.SaleId))).Should().Be(1);
        (await WithDbAsync(db => db.SaleLines.CountAsync(l => l.SaleId == request.SaleId))).Should().Be(1);
        (await OutboxCountAsync(request.SaleId, IntegrationEventRegistry.NameOf(typeof(SaleCompleted))))
            .Should().Be(1);
        await DrainAsync();
    }

    [Fact]
    public async Task Soma_diferente_recusa_no_canal_sem_caixa_aceita_com_divergencia_do_aparelho_e_valida_por_canal()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 2m);
        var day = Today.AddDays(-13);

        // CA 5: no canal sem caixa, quem chama ainda pode corrigir.
        var mismatch = await IngestAsync(Online(day, location, [Line(product, 1, 20m)], [Paid(methods.Pix, 19m)]));
        mismatch.IsFailure.Should().BeTrue();
        mismatch.Error!.Code.Should().Be("payment_total_mismatch");

        // Troco só em forma que dá troco (Pix não dá).
        (await IngestAsync(Online(day, location, [Line(product, 1, 20m)], [Paid(methods.Pix, 25m, change: 5m)]))).Error!.Message.Should().Contain("não dá troco");

        // CA 6: online não tem caixa; PDV exige caixa e sessão.
        var withRegister = Online(day, location, [Line(product, 1, 20m)], [Paid(methods.Pix, 20m)]) with { RegisterId = Guid.NewGuid() };
        (await IngestAsync(withRegister)).Error!.Code.Should().Be("sale_channel_invalid");
        var posWithout = Online(day, location, [Line(product, 1, 20m)], [Paid(methods.Pix, 20m)]) with { Channel = SaleChannel.Pos, FromDevice = true };
        (await IngestAsync(posWithout)).Error!.Fields!.Keys.Should().BeEquivalentTo(["registerId", "cashSessionId"]);

        // CA 5: a venda do aparelho do PDV nunca é recusada por divergência; entra marcada (OFF-02 registra a pendência).
        var device = Online(day, location, [Line(product, 1, 20m)], [Paid(methods.Pix, 19m)]) with
        {
            Channel = SaleChannel.Pos, RegisterId = Guid.NewGuid(), CashSessionId = Guid.NewGuid(), FromDevice = true,
        };
        var accepted = await IngestAsync(device);
        accepted.IsSuccess.Should().BeTrue(accepted.Error?.Message);
        accepted.Value.Outcome.Should().Be(SaleIngestionOutcome.AcceptedWithDivergence);
        accepted.Value.PaymentDifference.Should().Be(-1m);
        await DrainAsync();
        (await WithDbAsync(db => db.Sales.Where(s => s.Id == device.SaleId).Select(s => s.StockStatus).SingleAsync()))
            .Should().Be(SaleStockStatus.Pending, "venda com sessão de caixa fica para a baixa agregada do PDV (PDV-05)");
        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.CashSessionId == device.CashSessionId))).Should().Be(0, "o recebível do PDV é do fechamento (CX-03)");
    }

    [Fact]
    public async Task Seguranca_campo_livre_com_numero_de_cartao_e_recusado()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 2m);
        var payment = Paid(methods.Credit, 20m, brand: "Visa") with { Nsu = "4111 1111 1111 1111" };
        var refused = await IngestAsync(Online(Today.AddDays(-14), location, [Line(product, 1, 20m)], [payment]));
        refused.IsFailure.Should().BeTrue();
        refused.Error!.Message.Should().Contain("número de cartão");
        var note = await IngestAsync(Online(Today.AddDays(-14), location, [Line(product, 1, 20m) with { Note = "cartão 5555-5555-5555-4444" }], [Paid(methods.Pix, 20m)]));
        note.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Pedido_pago_em_duas_partes_pela_maquina_de_estados_conclui_no_ultimo_pagamento()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 2m);
        var order = new OrderSale(Guid.CreateVersion7(), SaleChannel.ServiceOrder, Unique("OS"), "ServiceOrder", Guid.NewGuid(), [Line(product, 2, 50m)], StockLocationId: location);

        async Task<SaleView> PayAsync(params OrderPaymentInput[] payments)
        {
            await using var scope = await ScopeAsync();
            var result = await scope.ServiceProvider.GetRequiredService<IOrderPayments>().PayAsync(new OrderPaymentRequest(order, payments));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value;
        }

        var first = new OrderPaymentInput(Guid.CreateVersion7(), methods.Pix, 40m, PartLabel: "Ana");
        var partial = await PayAsync(first);
        partial.Status.Should().Be(SaleStatus.PartiallyPaid);
        partial.Payments.Should().ContainSingle(p => p.Status == SalePaymentStatus.Captured && p.ConfirmedAt != null);
        partial.CashSessionId.Should().BeNull();
        (await OutboxCountAsync(order.SaleId)).Should().Be(0, "só conclui no último pagamento");

        // Mesmo pagamento de novo não grava duas vezes; o que passa do total é recusado.
        (await PayAsync(first)).Payments.Should().HaveCount(1);
        await using (var scope = await ScopeAsync())
        {
            var over = await scope.ServiceProvider.GetRequiredService<IOrderPayments>().PayAsync(new OrderPaymentRequest(order, [new OrderPaymentInput(Guid.CreateVersion7(), methods.Pix, 61m)]));
            over.Error!.Code.Should().Be("payment_exceeds_total");
        }

        var done = await PayAsync(new OrderPaymentInput(Guid.CreateVersion7(), methods.Credit, 60m, PartLabel: "Bruno", CardBrand: "Visa", Nsu: "123456", AuthorizationCode: "A1B2C3"));
        done.Status.Should().Be(SaleStatus.Completed);
        done.Parts.Select(p => p.Label).Should().BeEquivalentTo(["Ana", "Bruno"]);
        done.Parts.Should().OnlyContain(p => p.Paid);
        done.CapturedTotal.Should().Be(100m);
        (await OutboxCountAsync(order.SaleId)).Should().Be(1);
        await DrainAsync();
    }

    [Fact]
    public async Task Fabrica_nao_ve_vendas_e_Comercio_le_a_venda_sem_custo_para_quem_nao_tem_view_cost()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 3m);
        var request = Online(Today.AddDays(-15), location, [Line(product, 1, 10m)], [Paid(methods.Pix, 10m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue();
        await DrainAsync();

        var sale = await JsonAsyncOk(await owner.GetAsync("/api/v1/sales/" + request.SaleId));
        sale.GetProperty("lines")[0].GetProperty("unitCost").GetDecimal().Should().Be(3m, "o Proprietário tem sales.view_cost");

        await CommerceAsync(false);
        try
        {
            var denied = await owner.GetAsync("/api/v1/sales/" + request.SaleId);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fora do perfil Fábrica");
            (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("module_not_released");
            var me = await JsonAsync(await owner.GetAsync("/api/v1/me/permissions"));
            me.GetProperty("modules").EnumerateArray().Where(m => m.GetProperty("code").GetString() == "sales" && !(m.TryGetProperty("hidden", out var h) && h.GetBoolean()))
                .Should().BeEmpty("oculto para a fábrica (ADR 0046)");
        }
        finally
        {
            await CommerceAsync(true);
        }
    }

    private static async Task<System.Text.Json.JsonElement> JsonAsyncOk(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }
}
