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
/// o PIN errado diz as tentativas restantes (CA 12), e o cancelamento da venda paga tira o dinheiro da sessão, estorna pelo <c>SaleCancelled</c> e
/// avisa o Proprietário (CA 7 e 9).
/// </summary>
public partial class PosTests
{
    private const string ApprovalsPath = "/api/v1/pos/supervisor-approvals";

    private static object ApprovalBody(string action, Guid supervisor, string pin, Guid saleId, decimal before = 20m, decimal after = 0m) =>
        new { action, supervisorId = supervisor, pin, reason = "Cliente desistiu", saleId, lineId = (Guid?)null, beforeAmount = before, afterAmount = after };

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
        var self = await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", till.OperatorId, OwnPin, saleId));
        self.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(self)).Should().Be("supervisor_self");

        // CA 12: PIN errado conta e diz as tentativas restantes; sem a permissão da ação, recusa.
        var wrong = await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", supervisor, "000001", saleId));
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Restam 4 tentativas");
        (await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("ChangePrice", supervisor, OwnPin, saleId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 10: PIN certo grava a trilha (ação, operador, supervisor, motivo, antes e depois, venda) e devolve a aprovação.
        var granted = await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelItem", supervisor, OwnPin, saleId, 20m, 0m)));
        var approvalId = granted.GetProperty("approvalId").GetGuid();
        var row = await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId));
        (row.Action, row.OperatorId, row.SupervisorId, row.SaleId, row.Reason, row.BeforeAmount, row.AfterAmount, row.RegisterId)
            .Should().Be((SupervisorAction.CancelItem, till.OperatorId, supervisor, saleId, "Cliente desistiu", 20m, 0m, till.Register));
        row.UsedAt.Should().BeNull();
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityId == supervisor.ToString() && a.Action == "PosSupervisorPinAccepted"))).Should().BeTrue();

        // A venda sobe com a remoção aprovada: entra sem a pendência "Autorização sem supervisor" e a aprovação fica usada; a mesma aprovação não
        // serve para outra venda.
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        List<object> Removal(Guid sale, Guid firstLine) =>
        [
            Ev(sale, "LineAdded", now, new { line = new { lineId = Guid.CreateVersion7(), itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = "2", unitCents = till.PriceCents, priceTableVersion = till.PriceVersion, code = (string?)null, note = (string?)null } }),
            Ev(sale, "LineRemoved", now, new { lineId = firstLine, reason = "authorized", operatorId = till.OperatorId, authorizedBy = supervisor, approvalId }),
        ];
        var (_, approved) = SaleOf(till, session, now, extra: Removal, fixedSaleId: saleId);
        var (_, reused) = SaleOf(till, session, now, extra: Removal);
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
        var granted = await OkAsync(await op.PostAsJsonAsync(ApprovalsPath, ApprovalBody("CancelSale", supervisor, OwnPin, saleId, total, 0m)));
        var approvalId = granted.GetProperty("approvalId").GetGuid();
        var number = (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Number;
        var cancel = Seal(till, PosEventIngestion.CancelKind, new
        {
            saleId, number, cashSessionId = session, at = now.AddSeconds(5), operatorId = till.OperatorId, authorizedBy = supervisor, approvalId, reason = "Cliente desistiu",
            refundConfirmed = false, refundNsu = (string?)null,
        }, now.AddSeconds(5));
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { cancel } }));
        Outcomes(result)[IdOf(cancel)].Should().Be("Accepted");

        (await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId))).Status.Should().Be(SaleStatus.Cancelled);
        (await WithDbAsync(db => db.SessionSales.AsNoTracking().SingleAsync(s => s.SaleId == saleId))).CashAmount.Should().Be(0, "CA 7: o dinheiro sai da gaveta da sessão");
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId))).UsedAt.Should().NotBeNull();
        (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type.EndsWith("SaleCancelled") && m.Payload.Contains(saleId.ToString()))))
            .Should().Be(1, "o estoque e o financeiro reagem ao SaleCancelled");
        (await NotificationsAsync("Venda " + number)).Should().NotBeEmpty("CA 9: o Proprietário é avisado");
    }
}
