using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-01b, integração (Testcontainers + fake do Inter): emissão de ponta a ponta com o PDF guardado e o título intacto (CA 6, 7, 12,
/// 14), limites (CA 5), falhas e reenvio com consulta antes de criar (CA 8), duplicado, idempotência e concorrência (CA 9, 22),
/// produção (CA 11), vencido (CA 15).
/// </summary>
public partial class BillingSlipTests(BillingSlipFixture fixture) : IClassFixture<BillingSlipFixture>
{
    private Task<BillingSlip> SlipAsync(Guid id) => fixture.QueryAsync(db => db.BillingSlips.AsNoTracking().SingleAsync(s => s.Id == id));

    private Task<Receivable> ReceivableAsync(Guid id) => fixture.QueryAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == id));

    [Fact]
    public async Task Emitir_de_ponta_a_ponta_guarda_o_PDF_e_nao_muda_o_titulo()
    {
        var s = await fixture.ScenarioAsync(1234.56m);
        var before = await ReceivableAsync(s.ReceivableId);

        // CA 4 e 10: o diálogo vem com o saldo, o vencimento do título, multa e juros de Configurações e o e-mail de cobrança.
        var draft = await JsonAsync(await s.Owner.GetAsync(Slips(s.ReceivableId) + "/draft"));
        draft.GetProperty("amount").GetDecimal().Should().Be(1234.56m);
        draft.GetProperty("suggestedDueDate").GetString().Should().Be(Iso(Today.AddDays(10)));
        draft.GetProperty("finePct").GetDecimal().Should().Be(2m);
        draft.GetProperty("interestMonthPct").GetDecimal().Should().Be(1m);
        draft.GetProperty("email").GetString().Should().Be("cobranca@cliente-boleto.com.br");
        draft.GetProperty("autoEmail").GetBoolean().Should().BeTrue();
        draft.GetProperty("accounts").EnumerateArray().Should().Contain(a => a.GetProperty("id").GetGuid() == s.Account && a.GetProperty("sandbox").GetBoolean());
        draft.GetProperty("preconditions").EnumerateArray().Where(p => p.GetProperty("blocking").GetBoolean()).Should().BeEmpty();

        // CA 6: Requested, "seu número" em base 36 (curto e opaco), job só com ids, mensagem e o título ligado.
        var response = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, sendEmail: true);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var result = await JsonAsync(response);
        result.GetProperty("message").GetString().Should().Be(BillingSlipService.RequestedMessage);
        var slipId = result.GetProperty("slip").GetProperty("id").GetGuid();
        var requested = await SlipAsync(slipId);
        requested.Status.Should().Be(BillingSlipStatus.Requested);
        requested.OurReference.Should().MatchRegex("^[0-9A-Z]{6,15}$").And.NotContain(before.Number.Replace("CR-", string.Empty, StringComparison.Ordinal));
        requested.Amount.Should().Be(1234.56m);
        fixture.JobsFor(slipId).Should().ContainSingle().Which.Type.Should().Be<IssueBillingSlipJob>();
        (await ReceivableAsync(s.ReceivableId)).BillingSlipId.Should().Be(slipId);

        // CA 7: o job cria, consulta, guarda o PDF e passa a Issued; o pagador foi lido do Parties no envio.
        await fixture.IssueJobAsync(slipId);
        var issued = await SlipAsync(slipId);
        issued.Status.Should().Be(BillingSlipStatus.Issued);
        issued.DigitableLine.Should().NotBeNullOrEmpty();
        issued.Barcode.Should().NotBeNullOrEmpty();
        issued.PixCopyPaste.Should().StartWith("000201");
        issued.OurNumber.Should().Be("0001234567");
        var charge = fixture.Inter.Billing.ByReference(issued.OurReference)!;
        charge.Body["pagador"]!["cep"]!.GetValue<string>().Should().Be("01310100");
        charge.Body["multa"]!["taxa"]!.GetValue<decimal>().Should().Be(2m);
        charge.Body["mora"]!["codigo"]!.GetValue<string>().Should().Be("TAXAMENSAL");
        var pdf = await fixture.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == issued.PdfAttachmentId));
        pdf.OwnerType.Should().Be(BillingSlipService.AttachmentOwner);
        pdf.OwnerId.Should().Be(slipId);
        pdf.ContentType.Should().Be("application/pdf");

        // CA 14: o retorno guardado não tem o pagador; a API não devolve o payload.
        issued.ProviderPayload.Should().NotContain("pagador").And.NotContain("Avenida Paulista").And.NotContain("cobranca@");
        var view = await s.Owner.GetAsync($"{Slips(s.ReceivableId)}/{slipId}");
        view.Headers.CacheControl?.NoStore.Should().BeTrue();
        var viewText = await view.Content.ReadAsStringAsync();
        viewText.Should().NotContain("providerPayload").And.NotContain("Avenida Paulista").And.Contain(issued.DigitableLine);

        // O título não muda de valor, situação nem vencimento (CA 7).
        var after = await ReceivableAsync(s.ReceivableId);
        after.Amount.Should().Be(before.Amount);
        after.Status.Should().Be(before.Status);
        after.DueDate.Should().Be(before.DueDate);
        after.PaidAmount.Should().Be(before.PaidAmount);

        // CA 13: "Enviar por e-mail ao emitir" põe o e-mail na fila com o PDF anexado, ligado ao boleto, e só com o que pode ir.
        var log = await fixture.QueryAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.RelatedId == slipId));
        log.Template.Should().Be(EmailDispatchLimiter.BillingSlipTemplate);
        log.RelatedType.Should().Be(BillingSlipService.AttachmentOwner);
        log.AttachmentIds.Should().Equal(issued.PdfAttachmentId!.Value);
        log.To.Should().Be("cobranca@cliente-boleto.com.br");
        log.Model.Should().Contain(before.Number).And.Contain(issued.DigitableLine!).And.NotContain("Avenida");
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Link == BillingSlipService.Link(s.ReceivableId) && n.Title.Contains("emitido")))).Should().BeTrue();

        // A lista ganha o filtro "Com boleto" e a situação do boleto na linha.
        var list = await JsonAsync(await s.Owner.GetAsync("/api/v1/finance/receivables?withBillingSlip=true&pageSize=100"));
        list.GetProperty("page").GetProperty("items").EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == s.ReceivableId && r.GetProperty("billingSlipStatus").GetString() == "Issued")
            .And.OnlyContain(r => r.GetProperty("billingSlipId").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task Acima_do_limite_so_quem_aprova_emite()
    {
        var s = await fixture.ScenarioAsync(10_000m);
        var (_, editor) = await fixture.ActorAsync(false, "finance.view", "finance.edit");

        // CA 5: desconto acima de 5% do saldo ou de R$ 500,00, ou multa/juros acima dos de Configurações: 403 approval_required.
        var discount = await IssueAsync(editor, s.ReceivableId, s.Account, s.Version, discount: 600m, discountUntil: Today.AddDays(5));
        discount.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(discount)).Should().Be(BillingSlipService.ApprovalCode);
        var fine = await IssueAsync(editor, s.ReceivableId, s.Account, s.Version, fine: 2m, interest: 1m, discount: 100m, discountUntil: Today.AddDays(5));
        fine.StatusCode.Should().Be(HttpStatusCode.Created, "R$ 100,00 é 1% do saldo: dentro do limite");
        var withinId = (await JsonAsync(fine)).GetProperty("slip").GetProperty("id").GetGuid();
        (await SlipAsync(withinId)).ApprovedBy.Should().BeNull();

        // Quem tem finance.approve passa, e a aprovação fica no boleto (histórico).
        var other = await fixture.ReceivableAsync(s.Owner, s.Party, 10_000m);
        var approved = await IssueAsync(s.Owner, other.GetProperty("id").GetGuid(), s.Account, other.GetProperty("version").GetUInt32(), discount: 600m, discountUntil: Today.AddDays(5));
        approved.StatusCode.Should().Be(HttpStatusCode.Created, await approved.Content.ReadAsStringAsync());
        var approvedSlip = (await JsonAsync(approved)).GetProperty("slip");
        approvedSlip.GetProperty("approvedByName").ValueKind.Should().Be(JsonValueKind.String);
        (await SlipAsync(approvedSlip.GetProperty("id").GetGuid())).ApprovedBy.Should().Be(s.OwnerId);
    }

    [Fact]
    public async Task Falha_do_banco_nao_mexe_no_titulo_e_o_reenvio_consulta_antes_de_criar()
    {
        var s = await fixture.ScenarioAsync(500m);
        var before = await ReceivableAsync(s.ReceivableId);
        var created = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, sendEmail: false);
        var slipId = (await JsonAsync(created)).GetProperty("slip").GetProperty("id").GetGuid();

        // CA 8: recusa de validação do CEP → Failed com o motivo traduzido e "Corrigir cadastro do cliente"; o título fica como estava.
        fixture.Inter.Billing.RejectProperty = "pagador.cep";
        await fixture.IssueJobAsync(slipId);
        var failed = await SlipAsync(slipId);
        failed.Status.Should().Be(BillingSlipStatus.Failed);
        failed.FailureReason.Should().Be("O CEP do cliente não foi aceito pelo banco.");
        failed.FailureAction.Should().Be(BillingFailureActions.FixParty);
        var after = await ReceivableAsync(s.ReceivableId);
        (after.Amount, after.Status, after.DueDate, after.PaidAmount).Should().Be((before.Amount, before.Status, before.DueDate, before.PaidAmount));
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Title.Contains("O CEP do cliente não foi aceito pelo banco.")))).Should().BeTrue();

        // Com a falha à espera, outro "Emitir boleto" é recusado; "Tentar de novo" reusa o mesmo "seu número".
        var again = await IssueAsync(s.Owner, s.ReceivableId, s.Account, (await ReceivableAsync(s.ReceivableId)).Version);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(again)).Should().Be(BillingSlipService.FailedCode);

        // Tempo esgotado depois de o banco criar: falha; a nova tentativa acha a cobrança pelo "seu número" e não cria outra.
        (await s.Owner.PostAsync($"{Slips(s.ReceivableId)}/{slipId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SlipAsync(slipId)).OurReference.Should().Be(failed.OurReference);
        fixture.Inter.Billing.TimeoutAfterCreate = true;
        await fixture.IssueJobAsync(slipId);
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Failed);
        fixture.Inter.Billing.Charges.Values.Count(c => c.OurReference == failed.OurReference).Should().Be(1);
        var issueCalls = fixture.Inter.Billing.IssueCalls;

        (await s.Owner.PostAsync($"{Slips(s.ReceivableId)}/{slipId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.IssueJobAsync(slipId);
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Issued);
        fixture.Inter.Billing.IssueCalls.Should().Be(issueCalls, "consulta pelo seu número antes de criar outra cobrança");
        fixture.Inter.Billing.Charges.Values.Count(c => c.OurReference == failed.OurReference).Should().Be(1);

        // 5xx também vira falha com "Tentar de novo".
        var other = await fixture.ReceivableAsync(s.Owner, s.Party, 300m);
        var second = await IssueAsync(s.Owner, other.GetProperty("id").GetGuid(), s.Account, other.GetProperty("version").GetUInt32(), sendEmail: false);
        var secondId = (await JsonAsync(second)).GetProperty("slip").GetProperty("id").GetGuid();
        fixture.Inter.Billing.ServerErrors = 1;
        await fixture.IssueJobAsync(secondId);
        var unavailable = await SlipAsync(secondId);
        unavailable.Status.Should().Be(BillingSlipStatus.Failed);
        unavailable.FailureAction.Should().Be(BillingFailureActions.Retry);
    }

    [Fact]
    public async Task Boleto_duplicado_idempotencia_e_duas_emissoes_simultaneas()
    {
        var s = await fixture.ScenarioAsync(800m);
        var key = Guid.NewGuid();
        var first = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await JsonAsync(first)).GetProperty("slip").GetProperty("id").GetGuid();

        // CA 9: o mesmo Idempotency-Key devolve o boleto original; outro pedido, 409 billing_slip_exists.
        var replay = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, key);
        replay.IsSuccessStatusCode.Should().BeTrue();
        (await JsonAsync(replay)).GetProperty("slip").GetProperty("id").GetGuid().Should().Be(firstId);
        var duplicate = await IssueAsync(s.Owner, s.ReceivableId, s.Account, (await ReceivableAsync(s.ReceivableId)).Version);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(duplicate);
        Code(problem).Should().Be(BillingSlipService.ExistsCode);
        problem.GetProperty("detail").GetString().Should().Be("Este título já tem um boleto em aberto. Cancele o atual para emitir outro.");

        // CA 22: duas emissões simultâneas para o mesmo título: só uma cria.
        var other = await fixture.ReceivableAsync(s.Owner, s.Party, 800m);
        var id = other.GetProperty("id").GetGuid();
        var version = other.GetProperty("version").GetUInt32();
        var both = await Task.WhenAll(IssueAsync(s.Owner, id, s.Account, version), IssueAsync(s.Owner, id, s.Account, version));
        both.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        both.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await fixture.QueryAsync(db => db.BillingSlips.CountAsync(x => x.ReceivableId == id))).Should().Be(1);
    }

    [Fact]
    public async Task Producao_sem_a_flag_e_titulo_vencido()
    {
        var s = await fixture.ScenarioAsync(700m, due: Today.AddDays(-10));

        // CA 15: vencido exige vencimento novo ≥ hoje; o título mantém o vencimento e a multa e os juros contam da data original.
        var draft = await JsonAsync(await s.Owner.GetAsync(Slips(s.ReceivableId) + "/draft"));
        draft.GetProperty("isOverdue").GetBoolean().Should().BeTrue();
        DateOnly.Parse(draft.GetProperty("suggestedDueDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Should().BeAfter(Today);
        var past = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, due: Today.AddDays(-1));
        past.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(past)).GetProperty("errors").GetProperty("dueDate").ValueKind.Should().Be(JsonValueKind.Array);
        var ok = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, due: Today.AddDays(3));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var slip = await SlipAsync((await JsonAsync(ok)).GetProperty("slip").GetProperty("id").GetGuid());
        slip.FineFrom.Should().Be(Today.AddDays(-10));
        slip.DueDate.Should().Be(Today.AddDays(3));
        (await ReceivableAsync(s.ReceivableId)).DueDate.Should().Be(Today.AddDays(-10));

        // CA 11: conta em produção sem bank_production_allowed → 409 bank_production_not_allowed.
        await fixture.QueryAsync(db => db.BankIntegrations.Where(i => i.BankAccountId == s.Account).ExecuteUpdateAsync(u => u.SetProperty(i => i.Environment, BankEnvironment.Production)));
        try
        {
            var other = await fixture.ReceivableAsync(s.Owner, s.Party, 700m);
            var refused = await IssueAsync(s.Owner, other.GetProperty("id").GetGuid(), s.Account, other.GetProperty("version").GetUInt32());
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(refused)).Should().Be(BankEffectGuard.ProductionNotAllowed);
        }
        finally
        {
            await fixture.QueryAsync(db => db.BankIntegrations.Where(i => i.BankAccountId == s.Account).ExecuteUpdateAsync(u => u.SetProperty(i => i.Environment, BankEnvironment.Sandbox)));
        }
    }
}
