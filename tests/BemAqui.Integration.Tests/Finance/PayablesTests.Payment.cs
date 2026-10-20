using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class PayablesTests
{
    [Fact]
    public async Task Forma_de_pagamento_validada_mascarada_e_com_avisos()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, document) = await SupplierAsync(owner);
        var bad = await owner.SendAsync(Post("/api/v1/finance/payables", await BodyAsync(party, payment: new { kind = "Pix", pixKeyType = "Cpf", pixKey = "123", holderName = "X", holderDocument = document })));
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("Chave Pix inválida");

        var other = NewCnpj();
        var pix = await PayableAsync(owner, party, 1000m, payment: new { kind = "Pix", pixKeyType = "Email", pixKey = "Financeiro@Fornecedor.com.br", holderName = "Outra Empresa", holderDocument = other });
        pix.GetProperty("holderWarning").GetString().Should().Contain("não é o fornecedor", "CA 7");
        var id = pix.GetProperty("items")[0].GetProperty("id").GetGuid();
        var list = await owner.GetStringAsync("/api/v1/finance/payables?partyId=" + party);
        list.Should().Contain("\"paymentMethod\":\"Pix\"").And.NotContain("fornecedor.com.br", "a lista mostra só o tipo (CA 8)");
        var view = await JsonAsync(await owner.GetAsync($"/api/v1/finance/payables/{id}/payment-method"));
        view.GetProperty("details").GetProperty("pixKey").GetString().Should().NotContain("financeiro@", "mascarado");
        (await owner.GetStringAsync($"/api/v1/audit/Payable/{id}")).Should().NotContain("fornecedor.com.br", "dado de pagamento mascarado no histórico (CA 22)");

        var line = BoletoLineTests.BankLine(Today.AddDays(12), 999m);
        var detail = await JsonAsync(await owner.GetAsync($"/api/v1/finance/payables/{id}"));
        var boleto = await owner.PutAsJsonAsync($"/api/v1/finance/payables/{id}/payment-method", new { paymentDetails = new { kind = "Boleto", barcode = line }, version = Version(detail) });
        boleto.StatusCode.Should().Be(HttpStatusCode.OK, await boleto.Content.ReadAsStringAsync());
        (await JsonAsync(boleto)).GetProperty("boletoWarning").GetString().Should().Contain("diferente do título", "valor e vencimento do boleto comparados (CA 6)");
        (await QueryAsync(db => db.Payables.Where(p => p.Id == id).Select(p => p.PaymentMethodChangedAt).SingleAsync())).Should().NotBeNull();

        var ted = await owner.SendAsync(Post("/api/v1/finance/payables", await BodyAsync(party, payment: new { kind = "Ted", bankCode = "999", agency = "1", account = "1", accountType = "Checking", holderName = "F", holderDocument = document })));
        ted.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "banco fora da lista");
    }

    [Fact]
    public async Task Titulo_em_OP_trava_os_campos_financeiros_e_o_cancelamento()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner);
        var id = (await PayableAsync(owner, party)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await QueryAsync(async db => { var p = await db.Payables.SingleAsync(x => x.Id == id); p.Status = PayableStatus.AwaitingApproval; return await db.SaveChangesAsync(); });
        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/payables/{id}"));
        object Update(decimal amount, string description) => new { description, accountId = current.GetProperty("accountId").GetGuid(), costCenterId = (Guid?)null, amount, dueDate = current.GetProperty("dueDate").GetString(), retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, version = Version(current) };
        var locked = await owner.PutAsJsonAsync($"/api/v1/finance/payables/{id}", Update(2000m, "Serviço de teste"));
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await locked.Content.ReadAsStringAsync()).Should().Contain("payable_in_payment_order");
        (await owner.PutAsJsonAsync($"/api/v1/finance/payables/{id}/payment-method", new { paymentDetails = new { kind = "Other", description = "Débito" }, version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PostAsJsonAsync($"/api/v1/finance/payables/{id}/cancel", new { reason = "x", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PutAsJsonAsync($"/api/v1/finance/payables/{id}", Update(1000m, "Descrição nova"))).StatusCode.Should().Be(HttpStatusCode.OK, "descrição e vencimento continuam editáveis (CA 11)");
    }

    [Fact]
    public async Task Cancelamento_com_motivo_limite_aviso_e_relatorio()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, financial) = await ActorAsync("finance");
        var (party, _) = await SupplierAsync(owner);
        var big = (await PayableAsync(owner, party, 5000m)).GetProperty("items")[0];
        var denied = await financial.PostAsJsonAsync($"/api/v1/finance/payables/{big.GetProperty("id").GetGuid()}/cancel", new { reason = "Erro", version = Version(big) });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("cancel_requires_approval");
        var ok = await owner.PostAsJsonAsync($"/api/v1/finance/payables/{big.GetProperty("id").GetGuid()}/cancel", new { reason = "Nota cancelada", version = Version(big) });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var number = big.GetProperty("number").GetString()!;
        (await QueryAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Título a pagar " + number)))).Should().BeGreaterThan(0);
        var report = await owner.GetStringAsync("/api/v1/finance/receivables/discounts-and-cancellations");
        report.Should().Contain(number).And.Contain("\"isPayable\":true");
    }
}
