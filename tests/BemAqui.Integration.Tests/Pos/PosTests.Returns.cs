using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-06 com PostgreSQL real: devolução parcial com volta ao estoque e com perda (CA 4 e 6), vale-troca emitido e usado como forma de pagamento, com o
/// passivo no Financeiro (CA 7, 8 e 10), troca ligada à venda nova (CA 9), e prazo vencido e dinheiro com o supervisor (CA 3 e 5).
/// </summary>
public partial class PosTests
{
    private const string LookupPath = "/api/v1/pos/sales/lookup";
    private const string ReturnsPath = "/api/v1/pos/returns";
    private const string CreditsPath = "/api/v1/pos/store-credits";

    /// <summary>Caixa aberto com uma venda de 2 unidades em dinheiro já baixada no estoque.</summary>
    private async Task<(HttpClient Owner, Till Till, HttpClient Op, Guid Session, Guid SaleId, string Number, Guid LineId)> SoldAsync()
    {
        var (owner, till) = await TillAsync(true, "pos.return");
        var op = Operator(await PinTokenAsync(till.Device, till.OperatorId, OwnPin), till.Device);
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var (saleId, sale) = SaleOf(till, session, now, fixedLineId: lineId);
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1));
        var sold = Seal(till, PosEventIngestion.SaleKind, sale, now);
        Outcomes(await OkAsync(await PostEventsAsync(till.Device, new { events = new[] { open, sold } })))[IdOf(sold)].Should().Be("Accepted");
        await DrainOutboxAsync();
        var number = await WithDbAsync(db => db.Sales.AsNoTracking().Where(s => s.Id == saleId).Select(s => s.Number).SingleAsync());
        return (owner, till, op, session, saleId, number, lineId);
    }

    private static object ReturnBody(Guid saleId, Guid lineId, decimal quantity, string destination, string refund = "StoreCredit", bool exchange = false) =>
        new { saleId, lines = new[] { new { saleLineId = lineId, quantity, destination, reason = "Tamanho errado" } }, refundKind = refund, exchange };

    [Fact]
    public async Task Devolucao_parcial_volta_ao_estoque_e_perda_com_vale_troca_usado_na_venda_nova_ligada_a_troca()
    {
        var (owner, till, op, session, saleId, number, lineId) = await SoldAsync();
        using var device = till.Device;

        // CA 1: pelo número do cupom (no corpo); o CPF sem venda não acha nada; sem a venda, não há devolução.
        var found = await OkAsync(await op.PostAsJsonAsync(LookupPath, new { number }));
        var candidate = found.GetProperty("sales").EnumerateArray().Single();
        candidate.GetProperty("lines")[0].GetProperty("returnable").GetDecimal().Should().Be(2m);
        candidate.GetProperty("late").GetBoolean().Should().BeFalse();
        (await OkAsync(await op.PostAsJsonAsync(LookupPath, new { document = "529.982.247-25" }))).GetProperty("sales").GetArrayLength().Should().Be(0);

        // CA 4 e 6: 1 volta ao estoque, em troca (vale padrão); a venda fica parcialmente devolvida e o estoque recebe a entrada.
        var unit = till.PriceCents / 100m;
        var first = await OkAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock", exchange: true)));
        first.GetProperty("needsSupervisor").GetBoolean().Should().BeFalse();
        first.GetProperty("total").GetDecimal().Should().Be(unit);
        var firstId = first.GetProperty("id").GetGuid();
        var done = await OkAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{firstId}/approve", new { }));
        done.GetProperty("status").GetString().Should().Be("Completed");
        var code = done.GetProperty("storeCredit").GetProperty("code").GetString()!;
        code.Should().StartWith("VT-");
        (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Status.Should().Be(SaleStatus.PartiallyReturned);
        var restock = await WithDbAsync(db => db.SaleReturnLines.AsNoTracking().SingleAsync(l => l.ReturnId == firstId));
        var entry = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == restock.StockMovementId));
        (entry.Quantity, entry.SourceType).Should().Be((1m, StockSources.SaleReturn));
        entry.Type.Should().BeOneOf([StockMovementType.Return, StockMovementType.Adjustment], "entrada pelo custo da venda; sem custo, ajuste pelo custo médio");
        (await WithDbAsync(db => db.StoreCredits.AsNoTracking().SingleAsync(c => c.OriginReturnId == firstId))).CodeHash.Should().NotContain(code[3..7], "o código não fica no banco");

        // A outra unidade, avariada: entra e sai como perda; a venda fica devolvida e nada mais volta (CA 2).
        var second = await OkAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Damaged")));
        await OkAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{second.GetProperty("id").GetGuid()}/approve", new { }));
        var damaged = await WithDbAsync(db => db.SaleReturnLines.AsNoTracking().SingleAsync(l => l.ReturnId == second.GetProperty("id").GetGuid()));
        damaged.LossMovementId.Should().NotBeNull();
        (await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == damaged.LossMovementId))).Quantity.Should().Be(-1m);
        (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Status.Should().Be(SaleStatus.Returned);
        var over = await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock"));
        over.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await over.Content.ReadAsStringAsync()).Should().Contain("vendido menos o já devolvido");

        // CA 10: o vale nasce como passivo no Financeiro (Vales-troca → Devoluções de vendas), uma vez por devolução.
        await DrainOutboxAsync();
        var issued = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashMovementId == firstId));
        issued.Amount.Should().Be(unit);
        (await WithDbAsync(db => db.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == issued.FromAccountId))).Name.Should().Be("Vales-troca");

        // CA 8: consulta e uso com rede; o mesmo pagamento não abate duas vezes; o restante continua no vale.
        var credit = await OkAsync(await op.PostAsJsonAsync(CreditsPath + "/lookup", new { code }));
        (credit.GetProperty("balance").GetDecimal(), credit.GetProperty("usable").GetBoolean()).Should().Be((unit, true));
        (await op.PostAsJsonAsync(CreditsPath + "/lookup", new { code = "VT-2222-2222-2222" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var newSale = Guid.CreateVersion7();
        var tender = Guid.CreateVersion7();
        var use = unit - 1m;
        var redeemed = await OkAsync(await op.PostAsJsonAsync(CreditsPath + "/redeem", new { code, saleId = newSale, tenderId = tender, amount = use }));
        redeemed.GetProperty("balance").GetDecimal().Should().Be(1m);
        (await OkAsync(await op.PostAsJsonAsync(CreditsPath + "/redeem", new { code, saleId = newSale, tenderId = tender, amount = use }))).GetProperty("balance").GetDecimal().Should().Be(1m);
        (await JsonAsync(await op.PostAsJsonAsync(CreditsPath + "/redeem", new { code, saleId = newSale, tenderId = Guid.CreateVersion7(), amount = 5m })))
            .GetProperty("detail").GetString().Should().StartWith("O valor passa do saldo do vale");

        // A venda nova paga com o vale entra e fica ligada à devolução (troca, CA 9); com vale sem abatimento no servidor vai para a quarentena.
        var voucherMethod = await StoreCreditMethodAsync(owner);
        var withCredit = VoucherSale(till, session, newSale, voucherMethod, tender, (long)(use * 100));
        var forged = VoucherSale(till, session, Guid.CreateVersion7(), voucherMethod, Guid.CreateVersion7(), (long)(use * 100));
        var e1 = Seal(till, PosEventIngestion.SaleKind, withCredit, DateTimeOffset.UtcNow);
        var e2 = Seal(till, PosEventIngestion.SaleKind, forged, DateTimeOffset.UtcNow);
        var outcomes = Outcomes(await OkAsync(await PostEventsAsync(device, new { events = new[] { e1, e2 } })));
        outcomes[IdOf(e1)].Should().Be("Accepted");
        outcomes[IdOf(e2)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(e2))).Single().Summary.Should().Contain("sem o abatimento no servidor");
        await DrainOutboxAsync();
        (await WithDbAsync(db => db.SaleReturns.AsNoTracking().SingleAsync(r => r.Id == firstId))).ExchangeSaleId.Should().Be(newSale);
        (await WithDbAsync(db => db.StoreCreditMovements.AsNoTracking().SingleAsync(m => m.TenderId == tender))).ReversedAt.Should().BeNull();

        // Revisão de segurança (achado 3): a venda paga em parte com o vale devolve em dinheiro só até o que recebeu em dinheiro.
        var newLine = await WithDbAsync(db => db.SaleLines.AsNoTracking().Where(l => l.SaleId == newSale).Select(l => l.Id).SingleAsync());
        var overCash = await JsonAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(newSale, newLine, 2, "Restock", "Cash")));
        overCash.GetProperty("detail").GetString().Should().StartWith("Em dinheiro, devolva até");

        // Achado 4: o vale não abate em venda já concluída no servidor.
        (await JsonAsync(await op.PostAsJsonAsync(CreditsPath + "/redeem", new { code, saleId = newSale, tenderId = Guid.CreateVersion7(), amount = 0.5m })))
            .GetProperty("detail").GetString().Should().Be("O vale só abate em venda aberta deste caixa.");
    }

    [Fact]
    public async Task Fora_do_prazo_e_em_dinheiro_so_com_o_supervisor_e_o_dinheiro_sai_da_sessao()
    {
        var (owner, till, op, session, saleId, number, lineId) = await SoldAsync();
        using var device = till.Device;
        await WithDbAsync(db => db.Sales.Where(s => s.Id == saleId).ExecuteUpdateAsync(s => s.SetProperty(x => x.BusinessDate, x => x.BusinessDate.AddDays(-40))));

        // CA 3: fora do prazo da loja, a mensagem com a data; a conclusão pede o supervisor.
        var candidate = (await OkAsync(await op.PostAsJsonAsync(LookupPath, new { number }))).GetProperty("sales")[0];
        candidate.GetProperty("lateMessage").GetString().Should().StartWith("Prazo de devolução encerrado em ");
        var requested = await OkAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock", "Cash")));
        requested.GetProperty("needsSupervisor").GetBoolean().Should().BeTrue();
        var returnId = requested.GetProperty("id").GetGuid();
        var total = requested.GetProperty("total").GetDecimal();
        var refused = await JsonAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/approve", new { }));
        Code(refused).Should().Be("supervisor_required");
        refused.GetProperty("detail").GetString().Should().StartWith("Prazo de devolução encerrado em ");

        // CA 5: o supervisor (pos.return) aprova pelo PIN no caixa, preso à devolução e ao valor; o dinheiro sai da sessão e vira saída no Financeiro.
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.return"), till.Store, "Sara Supervisora");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device); // a entrada do supervisor no aparelho encerra a do operador
        var wrongValue = (await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, new
        {
            action = "Return", supervisorId = supervisor, pin = OwnPin, reason = "Cliente antigo", saleId = returnId, lineId = (Guid?)null, beforeAmount = total - 0.01m, afterAmount = 0m,
        }))).GetProperty("approvalId").GetGuid();
        Code(await JsonAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/approve", new { supervisorApprovalId = wrongValue }))).Should().Be("return_approval_invalid");
        var approval = (await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, new
        {
            action = "Return", supervisorId = supervisor, pin = OwnPin, reason = "Cliente antigo", saleId = returnId, lineId = (Guid?)null, beforeAmount = total, afterAmount = 0m,
        }))).GetProperty("approvalId").GetGuid();
        var done = await OkAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/approve", new { supervisorApprovalId = approval }));
        done.GetProperty("status").GetString().Should().Be("Completed");
        (await WithDbAsync(db => db.SaleReturns.AsNoTracking().SingleAsync(r => r.Id == returnId))).ApprovedBy.Should().Be(supervisor);
        var refund = await WithDbAsync(db => db.CashMovements.AsNoTracking().SingleAsync(m => m.Id == returnId));
        (refund.Kind, refund.Amount, refund.SessionId).Should().Be((CashMovementKind.Refund, total, session));
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approval))).UsedAt.Should().NotBeNull();
        await DrainOutboxAsync();
        var transfer = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashMovementId == returnId));
        (await WithDbAsync(db => db.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == transfer.ToAccountId))).Name.Should().Be("Devoluções de vendas");

        // A mesma aprovação não serve de novo, e a venda com devolução não é cancelada.
        var again = await OkAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock", "Cash")));
        Code(await JsonAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{again.GetProperty("id").GetGuid()}/approve", new { supervisorApprovalId = approval })))
            .Should().Be("return_approval_invalid");
    }

    [Fact]
    public async Task Revisao_de_seguranca_loja_da_venda_receita_sem_a_devolucao_e_reemissao_do_codigo_do_vale()
    {
        var (owner, till, op, _, saleId, number, lineId) = await SoldAsync();
        using var device = till.Device;
        var unit = till.PriceCents / 100m;
        var saleDate = await WithDbAsync(db => db.Sales.AsNoTracking().Where(s => s.Id == saleId).Select(s => s.BusinessDate).SingleAsync());
        async Task<(decimal Net, decimal Returns)> RevenueAsync()
        {
            await using var scope = await ScopeAsync();
            var lines = (await scope.ServiceProvider.GetRequiredService<ISalesRevenue>().RevenueAsync(saleDate.AddDays(-1), saleDate.AddDays(1))).Where(r => r.StoreId == till.Store).ToList();
            return (lines.Sum(r => r.Net), lines.Sum(r => r.Returns));
        }

        // Achado 2: em outra loja a venda não aparece nem é devolvida.
        var (_, other) = await TillAsync(true, "pos.return");
        using (other.Device)
        {
            var otherOp = Operator(await PinTokenAsync(other.Device, other.OperatorId, OwnPin), other.Device);
            (await OkAsync(await otherOp.PostAsJsonAsync(LookupPath, new { number }))).GetProperty("sales").GetArrayLength().Should().Be(0);
            (await otherOp.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // Achado 9: o cliente do vale vem da venda; o corpo não aceita outro.
        var forged = new { saleId, lines = new[] { new { saleLineId = lineId, quantity = 1m, destination = "Restock", reason = "Tamanho errado" } }, customerId = Guid.CreateVersion7() };
        (await op.PostAsJsonAsync(ReturnsPath, forged)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Achado 6: a devolução parcial (1 de 2, em vale) sai da receita do dia da devolução.
        var before = await RevenueAsync();
        var requested = await OkAsync(await op.PostAsJsonAsync(ReturnsPath, ReturnBody(saleId, lineId, 1, "Restock")));
        var returnId = requested.GetProperty("id").GetGuid();
        var done = await OkAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/approve", new { }));
        var oldCode = done.GetProperty("storeCredit").GetProperty("code").GetString()!;
        var after = await RevenueAsync();
        (after.Net, after.Returns).Should().Be((before.Net - unit, before.Returns + unit));

        // Achado 5: o código perdido é reemitido com o PIN do supervisor (preso à devolução e ao saldo); o antigo deixa de valer.
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.return"), till.Store, "Rui Supervisor");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device);
        Code(await JsonAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/store-credit/reissue", new { supervisorApprovalId = Guid.CreateVersion7() })))
            .Should().Be("return_approval_invalid");
        var approval = (await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, new
        {
            action = "CreditReissue", supervisorId = supervisor, pin = OwnPin, reason = "Cliente perdeu o vale", saleId = returnId, lineId = (Guid?)null, beforeAmount = unit, afterAmount = 0m,
        }))).GetProperty("approvalId").GetGuid();
        var reissued = await OkAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/store-credit/reissue", new { supervisorApprovalId = approval }));
        var newCode = reissued.GetProperty("code").GetString()!;
        newCode.Should().NotBe(oldCode);
        reissued.GetProperty("balance").GetDecimal().Should().Be(unit);
        (await op.PostAsJsonAsync(CreditsPath + "/lookup", new { code = oldCode })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OkAsync(await op.PostAsJsonAsync(CreditsPath + "/lookup", new { code = newCode }))).GetProperty("usable").GetBoolean().Should().BeTrue();
        Code(await JsonAsync(await op.PostAsJsonAsync($"{ReturnsPath}/{returnId}/store-credit/reissue", new { supervisorApprovalId = approval })))
            .Should().Be("return_approval_invalid", "a aprovação serve uma vez só");
    }

    /// <summary>A forma "Vale-troca" (StoreCredit) da loja, descida para o catálogo do caixa.</summary>
    private async Task<Guid> StoreCreditMethodAsync(HttpClient owner)
    {
        var (_, drawer, _, _) = await SetupAsync();
        var id = (await OkAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = Unique("Vale-troca "), type = "StoreCredit", destinationAccountId = drawer, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await DrainOutboxAsync();
        return id;
    }

    /// <summary>Venda de 2 unidades paga com o vale (<paramref name="voucherCents"/>) e o resto em dinheiro, sem troco.</summary>
    private static object VoucherSale(Till till, Guid session, Guid saleId, Guid voucherMethod, Guid tenderId, long voucherCents)
    {
        var at = DateTimeOffset.UtcNow;
        var op = till.OperatorId;
        var total = till.PriceCents * 2;
        object Tender(Guid id, Guid method, string name, string type, long amount) => new
        {
            tender = new
            {
                tenderId = id, partId = "main", status = "Captured", methodId = method, methodName = name, methodType = type, amountCents = amount, receivedCents = amount,
                changeCents = 0L, installments = (int?)null, provider = (string?)null, acquirerId = (string?)null, cardBrand = (string?)null, nsu = (string?)null,
                authorizationCode = (string?)null, txid = (string?)null, endToEndId = (string?)null, integration = "Manual", operatorAttested = true, needsReconciliation = false,
                note = (string?)null, confirmedBy = op, at,
            },
        };
        var events = new List<object>
        {
            Ev(saleId, "SaleStarted", at, new { operatorId = op }),
            Ev(saleId, "LineAdded", at, new { line = new { lineId = Guid.CreateVersion7(), itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = "2", unitCents = till.PriceCents, priceTableVersion = till.PriceVersion, code = (string?)null, note = (string?)null } }),
            Ev(saleId, "PaymentStarted", at, new { operatorId = op, totalCents = total }),
            Ev(saleId, "TenderCaptured", at, Tender(tenderId, voucherMethod, "Vale-troca", "StoreCredit", voucherCents)),
            Ev(saleId, "TenderCaptured", at, Tender(Guid.CreateVersion7(), till.CashId, "Dinheiro", "Cash", total - voucherCents)),
            Ev(saleId, "SaleCompleted", at, new { operatorId = op, totalCents = total, paidCents = total, changeCents = 0L }),
        };
        till.SaleSeq++;
        var number = till.Prefix + till.SaleSeq.ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
        return new { saleId, number, status = "completed", totalCents = total, changeCents = 0L, cashSessionId = session, events };
    }
}
