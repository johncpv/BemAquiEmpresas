using System.Net;
using System.Net.Http.Json;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-04 com PostgreSQL real: o PIN do supervisor no caixa grava a trilha e libera só aquela ação (CA 10), o supervisor nunca é o operador (CA 11),
/// o PIN errado diz as tentativas restantes e o quinto bloqueia (CA 12), a venda de outra sessão vai para a devolução (CA 8), e o cancelamento da venda
/// paga tira o dinheiro da sessão, estorna pelo <c>SaleCancelled</c> e avisa o Proprietário (CA 7 e 9). Mais a revisão de segurança: a aprovação presa à
/// linha, aos valores e ao prazo (achados 1 e 6), autorização de outra pessoa sem a aprovação vai para a quarentena (achado 2), quem não aprova a ação
/// não tem o PIN conferido nem conta falha (achado 4) e os valores combinam com a ação (achado 7).
/// </summary>
public partial class PosTests
{
    private const string ApprovalsPath = "/api/v1/pos/supervisor-approvals";

    private static object ApprovalBody(string action, Guid supervisor, string pin, Guid saleId, decimal before = 20m, decimal after = 0m, Guid? lineId = null) =>
        new { action, supervisorId = supervisor, pin, reason = "Cliente desistiu", saleId, lineId, beforeAmount = before, afterAmount = after };

    [Fact]
    public async Task Supervisor_aprova_com_PIN_no_caixa_grava_a_trilha_e_libera_so_aquela_acao()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cancel_item", "pos.discount"), till.Store, "Sofia Supervisora");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        var op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device);

        var supervisors = await OkAsync(await op.GetAsync(ApprovalsPath + "/supervisors"));
        supervisors.EnumerateArray().Should().Contain(s => s.GetProperty("userId").GetGuid() == supervisor)
            .And.NotContain(s => s.GetProperty("userId").GetGuid() == till.OperatorId, "quem pede não aparece na lista");

        // CA 11: o operador não aprova a própria ação.
        var saleId = Guid.CreateVersion7();
        var firstLine = Guid.CreateVersion7();
        var self = await JsonAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", till.OperatorId, OwnPin, saleId)));
        Code(self).Should().Be("supervisor_self");
        self.GetProperty("detail").GetString().Should().Be("Outra pessoa precisa aprovar.");

        // Achado 7: os valores combinam com a ação (desconto não aumenta o valor).
        var upside = await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("Discount", supervisor, OwnPin, saleId, 10m, 12m));
        upside.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 12: PIN errado conta e diz as tentativas restantes.
        var wrong = await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", supervisor, "000001", saleId));
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Restam 4 tentativas");

        // Achado 4: sem a ação pedida entre as de supervisor, o PIN nem é conferido: a resposta é a do PIN errado e não conta falha (nem com o PIN certo).
        var devicePinFailures = await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().Where(d => d.Id == till.DeviceId).Select(d => d.PinFailures).SingleAsync());
        foreach (var pin in new[] { OwnPin, "000002" })
        {
            var refused = await JsonAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("ChangePrice", supervisor, pin, saleId, 10m, 8m, firstLine)));
            Code(refused).Should().Be("auth.pin_invalid");
            refused.GetProperty("detail").GetString().Should().Be("PIN incorreto. Confira e tente de novo.");
        }

        (await WithDbAsync(db => db.PosPins.AsNoTracking().SingleAsync(p => p.UserId == supervisor))).FailedCount.Should().Be(1, "só o PIN errado da ação que ele aprova contou");
        (await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().Where(d => d.Id == till.DeviceId).Select(d => d.PinFailures).SingleAsync()))
            .Should().Be(devicePinFailures, "o aparelho também não conta");

        // CA 10: PIN certo grava a trilha (ação, operador, supervisor, motivo, antes e depois, venda e linha) e devolve a aprovação.
        var lineValue = 2 * till.PriceCents / 100m;
        var granted = await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", supervisor, OwnPin, saleId, lineValue, 0m, firstLine)));
        var approvalId = granted.GetProperty("approvalId").GetGuid();
        var row = await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId));
        (row.Action, row.OperatorId, row.SupervisorId, row.SaleId, row.LineId, row.Reason, row.BeforeAmount, row.AfterAmount, row.RegisterId)
            .Should().Be((SupervisorAction.CancelItem, till.OperatorId, supervisor, saleId, (Guid?)firstLine, "Cliente desistiu", lineValue, 0m, till.Register));
        row.UsedAt.Should().BeNull();
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityId == supervisor.ToString() && a.Action == "PosSupervisorPinAccepted"))).Should().BeTrue();

        // A venda sobe com a remoção aprovada: entra e a aprovação fica usada; a mesma aprovação não serve para outra venda.
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var (_, approved) = SaleOf(till, session, now, extra: (sale, line) => Removal(till, sale, line, now, supervisor, approvalId), fixedSaleId: saleId, fixedLineId: firstLine);
        var (_, reused) = SaleOf(till, session, now, extra: (sale, line) => Removal(till, sale, line, now, supervisor, approvalId));
        var e1 = Seal(till, PosEventIngestion.SaleKind, approved, now);
        var e2 = Seal(till, PosEventIngestion.SaleKind, reused, now);
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1)), e1, e2 } }));
        var outcomes = Outcomes(result);
        outcomes[IdOf(e1)].Should().Be("Accepted");
        outcomes[IdOf(e2)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(e2))).Single().Summary.Should().Contain("aprovação de supervisor que não confere");
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId))).UsedByEventId.Should().Be(IdOf(e1));

        // CA 13: o relatório da retaguarda mostra a aprovação por operador, supervisor e tipo.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await OkAsync(await owner.GetAsync($"{ApprovalsPath}?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}&storeId={till.Store}"));
        report.GetProperty("rows").EnumerateArray().Should().ContainSingle(r => r.GetProperty("id").GetGuid() == approvalId && r.GetProperty("used").GetBoolean());
        report.GetProperty("byAction").EnumerateArray().Should().Contain(g => g.GetProperty("key").GetString() == "CancelItem");
    }

    [Fact]
    public async Task Aprovacao_presa_a_linha_aos_valores_e_ao_prazo_e_autorizacao_sem_aprovacao_vai_para_a_quarentena()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cancel_item"), till.Store, "Rita Supervisora");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        var op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device);
        var lineValue = 2 * till.PriceCents / 100m;

        async Task<Guid> ApproveAsync(Guid saleId, Guid? lineId, decimal before) =>
            (await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", supervisor, OwnPin, saleId, before, 0m, lineId)))).GetProperty("approvalId").GetGuid();

        var (wrongValueSale, wrongValueLine) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var wrongValue = await ApproveAsync(wrongValueSale, wrongValueLine, lineValue - 0.01m);
        var (wrongLineSale, wrongLineLine) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var wrongLine = await ApproveAsync(wrongLineSale, null, lineValue);
        var (expiredSale, expiredLine) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var expired = await ApproveAsync(expiredSale, expiredLine, lineValue);
        await WithDbAsync(db => db.SupervisorApprovals.Where(a => a.Id == expired)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ApprovedAt, DateTimeOffset.UtcNow - SupervisorApproval.MaxUseDelay - TimeSpan.FromMinutes(1))));

        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        object SaleWith(Guid saleId, Guid lineId, Guid? approvalId) =>
            SaleOf(till, session, now, extra: (sale, line) => Removal(till, sale, line, now, supervisor, approvalId), fixedSaleId: saleId, fixedLineId: lineId).Payload;

        var cases = new (object Event, string Expected)[]
        {
            (Seal(till, PosEventIngestion.SaleKind, SaleWith(wrongValueSale, wrongValueLine, wrongValue), now), "valores diferentes dos aprovados"),
            (Seal(till, PosEventIngestion.SaleKind, SaleWith(wrongLineSale, wrongLineLine, wrongLine), now), "aprovação de supervisor que não confere"),
            (Seal(till, PosEventIngestion.SaleKind, SaleWith(expiredSale, expiredLine, expired), now), "minutos depois da aprovação"),
            (Seal(till, PosEventIngestion.SaleKind, SaleWith(Guid.CreateVersion7(), Guid.CreateVersion7(), null), now), "sem a aprovação do supervisor"),
        };
        var events = new List<object> { Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1)) };
        events.AddRange(cases.Select(c => c.Event));
        var outcomes = Outcomes(await OkAsync(await PostEventsAsync(device, new { events })));
        foreach (var (sealedEvent, expected) in cases)
        {
            outcomes[IdOf(sealedEvent)].Should().Be("Quarantined", expected);
            (await DivergencesAsync(IdOf(sealedEvent))).Single().Summary.Should().Contain(expected);
        }

        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().CountAsync(a => (a.Id == wrongValue || a.Id == wrongLine || a.Id == expired) && a.UsedAt != null)))
            .Should().Be(0, "aprovação recusada não fica usada");
    }

    [Fact]
    public async Task Quinto_PIN_errado_do_supervisor_bloqueia_e_venda_de_outra_sessao_vai_para_a_devolucao()
    {
        var (owner, till) = await TillAsync(true, "pos.cancel_sale");
        using var device = till.Device;
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cancel_sale"), till.Store, "Bia Gerente");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        var op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device);

        // CA 8: a venda é de uma sessão que não está mais aberta.
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var (saleId, sale) = SaleOf(till, session, now);
        await OkAsync(await PostEventsAsync(device, new
        {
            events = new[] { Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1)), Seal(till, PosEventIngestion.SaleKind, sale, now) },
        }));
        await WithDbAsync(db => db.CashSessions.Where(s => s.Id == session).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CashSessionStatus.Closed)));
        var total = 2 * till.PriceCents / 100m;
        var other = await JsonAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, OwnPin, saleId, total, 0m)));
        Code(other).Should().Be("sale_other_register");
        other.GetProperty("detail").GetString().Should().Be("Esta venda é de outro caixa. Use a devolução.");

        // CA 12: o quinto PIN errado bloqueia o PIN do supervisor neste aparelho; depois disso, nem o certo passa.
        var codes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            codes.Add(Code(await JsonAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, "00000" + i, saleId, total, 0m)))));
        }

        codes.Should().Equal("auth.pin_invalid", "auth.pin_invalid", "auth.pin_invalid", "auth.pin_invalid", "auth.pin_locked");
        Code(await JsonAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, OwnPin, saleId, total, 0m)))).Should().Be("auth.pin_locked");
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().CountAsync(a => a.SaleId == saleId))).Should().Be(0);
    }

    [Fact]
    public async Task Cancelamento_da_venda_paga_tira_o_dinheiro_da_sessao_estorna_e_avisa_o_Proprietario()
    {
        var (owner, till) = await TillAsync(true, "pos.cancel_sale");
        using var device = till.Device;
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cancel_sale"), till.Store, "Gina Gerente");
        await OkAsync(await PinLoginAsync(device, supervisor, InitialPin, OwnPin));
        var op = Operator(await PinTokenAsync(device, till.OperatorId, OwnPin), device);
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var (saleId, sale) = SaleOf(till, session, now);
        await OkAsync(await PostEventsAsync(device, new
        {
            events = new[] { Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1)), Seal(till, PosEventIngestion.SaleKind, sale, now) },
        }));
        (await WithDbAsync(db => db.SessionSales.AsNoTracking().SingleAsync(s => s.SaleId == saleId))).CashAmount.Should().BeGreaterThan(0);

        var total = 2 * till.PriceCents / 100m;
        var number = (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Number;
        object CancelOf(Guid approval, int seconds) => Seal(till, PosEventIngestion.CancelKind, new
        {
            saleId, number, cashSessionId = session, at = now.AddSeconds(seconds), operatorId = till.OperatorId, authorizedBy = supervisor, approvalId = approval,
            reason = "Cliente desistiu", refundConfirmed = false, refundNsu = (string?)null,
        }, now.AddSeconds(seconds));

        // Achado 1: a aprovação de outro valor não cancela a venda.
        var cheaper = (await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, OwnPin, saleId, total - 1m, 0m)))).GetProperty("approvalId").GetGuid();
        var refused = CancelOf(cheaper, 4);
        Outcomes(await OkAsync(await PostEventsAsync(device, new { events = new[] { refused } })))[IdOf(refused)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(refused))).Single().Summary.Should().Contain("outro valor");

        var granted = await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, OwnPin, saleId, total, 0m)));
        var approvalId = granted.GetProperty("approvalId").GetGuid();
        var cancel = CancelOf(approvalId, 5);
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { cancel } }));
        Outcomes(result)[IdOf(cancel)].Should().Be("Accepted");

        (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Status.Should().Be(SaleStatus.Cancelled);
        (await WithDbAsync(db => db.SessionSales.AsNoTracking().SingleAsync(s => s.SaleId == saleId))).CashAmount.Should().Be(0, "CA 7: o dinheiro sai da gaveta da sessão");
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId))).UsedAt.Should().NotBeNull();
        (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type.EndsWith("SaleCancelled") && m.Payload.Contains(saleId.ToString()))))
            .Should().Be(1, "o estoque e o financeiro reagem ao SaleCancelled");
        (await NotificationsAsync("Venda " + number)).Should().NotBeEmpty("CA 9: o Proprietário é avisado");
    }

    /// <summary>Uma linha nova e a remoção da primeira com a autorização do supervisor (com a aprovação, ou sem ela quando <paramref name="approvalId"/> é nulo).</summary>
    private static List<object> Removal(Till till, Guid sale, Guid firstLine, DateTimeOffset now, Guid supervisor, Guid? approvalId) =>
    [
        Ev(sale, "LineAdded", now, new { line = new { lineId = Guid.CreateVersion7(), itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = "2", unitCents = till.PriceCents, priceTableVersion = till.PriceVersion, code = (string?)null, note = (string?)null } }),
        Ev(sale, "LineRemoved", now, new { lineId = firstLine, reason = "authorized", operatorId = till.OperatorId, authorizedBy = supervisor, approvalId }),
    ];
}
